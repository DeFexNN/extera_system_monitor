#include <ntddk.h>
#include <intrin.h>

// The driver transports IEEE-754 PM-table values but never performs floating
// point arithmetic in kernel mode. Satisfy the CRT marker emitted by MSVC.
int _fltused = 0;

#define DEVICE_NAME L"\\Device\\ExteraMonitorDriver"
#define DOS_NAME    L"\\DosDevices\\ExteraMonitorDriver"
#define IOCTL_EXTERA_GET_TEMPERATURE CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_READ_DATA)
#define IOCTL_EXTERA_GET_TEMPERATURE_ANY CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_EXTERA_GET_TELEMETRY CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define EXTERA_MAX_CCDS 8
#define EXTERA_MAX_PROCESSORS 64
#define ZEN_SMN_TEMPERATURE 0x00059800
#define ZEN_CCD_TEMPERATURE_BASE 0x00059800
#define ZEN_CCD_OFFSET_ZEN4 0x00000308
#define ZEN_CCD_VALID (1u << 11)
#define ZEN_CCD_MASK 0x7FFu
#define ZEN_TEMP_RANGE_SELECT (1u << 19)

// Raphael (Zen 4) read-only SMU PM-table path. The mailbox is reached via
// the AMD root function's indirect SMN window. Only the fixed query commands
// below are used; no tuning, voltage, or power-limit commands are exposed.
#define SMU_PCI_ADDR_REG 0xC4
#define SMU_PCI_DATA_REG 0xC8
#define SMU_RSMU_CMD 0x03B10524
#define SMU_RSMU_RSP 0x03B10570
#define SMU_RSMU_ARGS 0x03B10A40
#define SMU_RETRIES 4096
#define RAPHAEL_PM_TABLE_VERSION 0x00540004
#define RAPHAEL_PM_TABLE_VERSION_ALT 0x00540104
#define RAPHAEL_PM_TABLE_SIZE 0x948
#define RAPHAEL_PM_VERSION_COMMAND 0x05
#define RAPHAEL_PM_BASE_COMMAND 0x04
#define RAPHAEL_PM_TRANSFER_COMMAND 0x03

// Read-only AMD telemetry MSRs used by LibreHardwareMonitor's Zen 4 path.
#define AMD_MSR_APERF 0xC00000E8
#define AMD_MSR_MPERF 0xC00000E7
#define AMD_MSR_CORE_ENERGY 0xC001029A
#define AMD_MSR_PACKAGE_ENERGY 0xC001029B
#define AMD_MSR_POWER_UNIT 0xC0010299
#define AMD_MSR_PSTATE_STATUS 0xC0010293

typedef struct _EXTERA_MONITOR_TEMPERATURE {
    LONG CelsiusMilli;
    ULONG RawRegister;
    ULONG PciBus;
    ULONG PciSlot;
    NTSTATUS DriverStatus;
} EXTERA_MONITOR_TEMPERATURE, *PEXTERA_MONITOR_TEMPERATURE;

typedef struct _EXTERA_CORE_TELEMETRY {
    ULONG ProcessorIndex;
    ULONG Valid;
    ULONGLONG Aperf;
    ULONGLONG Mperf;
    ULONG CoreEnergy;
    ULONG Pstate;
} EXTERA_CORE_TELEMETRY, *PEXTERA_CORE_TELEMETRY;

typedef struct _EXTERA_MONITOR_TELEMETRY {
    ULONG Version;
    ULONG CpuFamily;
    ULONG CpuModel;
    ULONG ProcessorCount;
    ULONG CcdCount;
    ULONG PowerUnitRaw;
    ULONG Capabilities;
    NTSTATUS DriverStatus;
    LARGE_INTEGER QueryPerformanceCounter;
    LARGE_INTEGER QueryPerformanceFrequency;
    ULONG PackageEnergy;
    ULONG PstateStatus;
    LONG CelsiusMilli;
    ULONG RawRegister;
    ULONG PciBus;
    ULONG PciSlot;
    LONG CcdCelsiusMilli[EXTERA_MAX_CCDS];
    EXTERA_CORE_TELEMETRY Cores[EXTERA_MAX_PROCESSORS];
    ULONG SmuStatus;
    ULONG PmTableVersion;
    ULONG PmTableSize;
    ULONG PmCapabilities;
    float PmPpt;
    float PmPackageTemperature;
    float PmCorePower;
    float PmSocPower;
    float PmMiscPower;
    float PmTotalPower;
    float PmVddcr;
    float PmTdc;
    float PmEdc;
    float PmVddcrSoc;
    float PmVddMisc;
    float PmFabricClock;
    float PmUncoreClock;
    float PmMemoryClock;
    float PmIodHotspot;
    float PmCcd1Temperature;
    float PmCcd2Temperature;
    float PmLdoVdd;
} EXTERA_MONITOR_TELEMETRY, *PEXTERA_MONITOR_TELEMETRY;

static PDEVICE_OBJECT g_Device;
static FAST_MUTEX g_PciMutex;
static PVOID g_PmTableMapping;
static ULONGLONG g_PmTablePhysicalBase;

static ULONG PciRead32(UCHAR bus, UCHAR slot, ULONG offset)
{
    ULONG value = 0xFFFFFFFF;
    HalGetBusDataByOffset(PCIConfiguration, bus, slot, &value, offset, sizeof(value));
    return value;
}

static BOOLEAN PciWrite32(UCHAR bus, UCHAR slot, ULONG offset, ULONG value)
{
    return HalSetBusDataByOffset(PCIConfiguration, bus, slot, &value, offset, sizeof(value)) == sizeof(value);
}

static BOOLEAN FindAmdDataFabric(UCHAR* outBus, UCHAR* outSlot)
{
    for (ULONG bus = 0; bus < 256; ++bus) {
        for (ULONG device = 0; device < 32; ++device) {
            for (ULONG function = 0; function < 8; ++function) {
                UCHAR slot = (UCHAR)((device << 3) | function);
                ULONG id = PciRead32((UCHAR)bus, slot, 0);
                USHORT vendor = (USHORT)(id & 0xFFFF);
                USHORT deviceId = (USHORT)(id >> 16);
                if (vendor == 0x1022 &&
                    (deviceId == 0x14E3 || deviceId == 0x14F3 || deviceId == 0x1653 ||
                     deviceId == 0x14B0 || deviceId == 0x167C || deviceId == 0x166D)) {
                    *outBus = (UCHAR)bus;
                    *outSlot = slot;
                    return TRUE;
                }
            }
        }
    }
    return FALSE;
}

static BOOLEAN ReadSmn32(UCHAR bus, UCHAR slot, ULONG address, PULONG value)
{
    if (!PciWrite32(bus, slot, 0x60, address)) return FALSE;
    *value = PciRead32(bus, slot, 0x64);
    return *value != 0xFFFFFFFF;
}

static BOOLEAN ReadSmuRegister(UCHAR bus, UCHAR slot, ULONG address, PULONG value)
{
    if (!PciWrite32(bus, slot, SMU_PCI_ADDR_REG, address)) return FALSE;
    *value = PciRead32(bus, slot, SMU_PCI_DATA_REG);
    return *value != 0xFFFFFFFF;
}

static BOOLEAN WriteSmuRegister(UCHAR bus, UCHAR slot, ULONG address, ULONG value)
{
    if (!PciWrite32(bus, slot, SMU_PCI_ADDR_REG, address)) return FALSE;
    return PciWrite32(bus, slot, SMU_PCI_DATA_REG, value);
}

static BOOLEAN SendRaphaelReadOnlyCommand(UCHAR bus, UCHAR slot, ULONG command, PULONG args)
{
    ULONG response = 0;
    BOOLEAN ready = FALSE;
    for (ULONG attempt = 0; attempt < SMU_RETRIES; ++attempt) {
        if (!ReadSmuRegister(bus, slot, SMU_RSMU_RSP, &response)) return FALSE;
        if (response != 0) { ready = TRUE; break; }
        KeStallExecutionProcessor(1);
    }
    if (!ready || !WriteSmuRegister(bus, slot, SMU_RSMU_RSP, 0)) return FALSE;
    for (ULONG index = 0; index < 6; ++index)
        if (!WriteSmuRegister(bus, slot, SMU_RSMU_ARGS + (index * 4), args[index])) return FALSE;
    if (!WriteSmuRegister(bus, slot, SMU_RSMU_CMD, command)) return FALSE;

    response = 0;
    for (ULONG attempt = 0; attempt < SMU_RETRIES; ++attempt) {
        if (!ReadSmuRegister(bus, slot, SMU_RSMU_RSP, &response)) return FALSE;
        if (response != 0) { ready = TRUE; break; }
        KeStallExecutionProcessor(1);
    }
    if (!ready || response != 1) return FALSE;
    for (ULONG index = 0; index < 6; ++index)
        if (!ReadSmuRegister(bus, slot, SMU_RSMU_ARGS + (index * 4), &args[index])) return FALSE;
    return TRUE;
}

static BOOLEAN PmFloatValid(float value)
{
    return value == value && value > -100000.0f && value < 100000.0f;
}

static BOOLEAN ReadRaphaelPmTable(UCHAR bus, UCHAR slot, PEXTERA_MONITOR_TELEMETRY result)
{
    if (result->CpuFamily != 0x19 || result->CpuModel != 0x61) return FALSE;

    result->SmuStatus = 10;
    ULONG args[6] = { 0 };
    if (!SendRaphaelReadOnlyCommand(bus, slot, RAPHAEL_PM_VERSION_COMMAND, args)) return FALSE;
    result->SmuStatus = 11;
    result->PmTableVersion = args[0];
    ULONG tableSize = 0;
    if (args[0] == RAPHAEL_PM_TABLE_VERSION) tableSize = RAPHAEL_PM_TABLE_SIZE;
    else if (args[0] == RAPHAEL_PM_TABLE_VERSION_ALT) tableSize = 0x950;
    else return FALSE;

    RtlZeroMemory(args, sizeof(args));
    args[0] = 1;
    args[1] = 1;
    if (!SendRaphaelReadOnlyCommand(bus, slot, RAPHAEL_PM_BASE_COMMAND, args)) return FALSE;
    result->SmuStatus = 12;
    ULONGLONG physicalBase = ((ULONGLONG)args[1] << 32) | args[0];
    if (physicalBase == 0 || (physicalBase & 3) != 0) return FALSE;

    if (g_PmTableMapping == NULL || g_PmTablePhysicalBase != physicalBase) {
        if (g_PmTableMapping != NULL) {
            MmUnmapIoSpace(g_PmTableMapping, PAGE_SIZE);
            g_PmTableMapping = NULL;
        }
        PHYSICAL_ADDRESS address;
        address.QuadPart = physicalBase;
        g_PmTableMapping = MmMapIoSpace(address, PAGE_SIZE, MmNonCached);
        if (g_PmTableMapping == NULL) return FALSE;
        g_PmTablePhysicalBase = physicalBase;
    }
    result->SmuStatus = 13;

    RtlZeroMemory(args, sizeof(args));
    if (!SendRaphaelReadOnlyCommand(bus, slot, RAPHAEL_PM_TRANSFER_COMMAND, args)) return FALSE;
    result->SmuStatus = 14;

    volatile ULONG* table = (volatile ULONG*)g_PmTableMapping;
    float values[596];
    for (ULONG index = 0; index < 596; ++index) {
        ULONG raw = table[index];
        RtlCopyMemory(&values[index], &raw, sizeof(raw));
    }
    result->PmTableSize = tableSize;
    result->PmCapabilities = 1;
    result->PmPpt = values[3];
    result->PmPackageTemperature = values[11];
    result->PmCorePower = values[20];
    result->PmSocPower = values[21];
    result->PmMiscPower = values[22];
    result->PmTotalPower = values[26];
    result->PmVddcr = values[47];
    result->PmTdc = values[48];
    result->PmEdc = values[49];
    result->PmVddcrSoc = values[52];
    result->PmVddMisc = values[57];
    result->PmFabricClock = values[70];
    result->PmUncoreClock = values[74];
    result->PmMemoryClock = values[78];
    result->PmIodHotspot = values[211];
    result->PmCcd1Temperature = values[539];
    result->PmCcd2Temperature = values[540];
    result->PmLdoVdd = values[268];
    if (!PmFloatValid(result->PmPpt) && !PmFloatValid(result->PmPackageTemperature)) {
        result->PmCapabilities = 0;
        return FALSE;
    }
    result->SmuStatus = 15;
    return TRUE;
}

static BOOLEAN ReadAllowedMsr(ULONG msr, PULONGLONG value)
{
    __try {
        *value = __readmsr(msr);
        return TRUE;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *value = 0;
        return FALSE;
    }
}

static VOID ReadCpuIdentity(PULONG family, PULONG model)
{
    int regs[4] = { 0 };
    __cpuidex(regs, 1, 0);
    ULONG eax = (ULONG)regs[0];
    ULONG baseFamily = (eax >> 8) & 0xF;
    ULONG extendedFamily = (eax >> 20) & 0xFF;
    ULONG baseModel = (eax >> 4) & 0xF;
    ULONG extendedModel = (eax >> 16) & 0xF;
    *family = baseFamily == 0xF ? baseFamily + extendedFamily : baseFamily;
    *model = (baseFamily == 0x6 || baseFamily == 0xF) ? baseModel | (extendedModel << 4) : baseModel;
}

static LONG DecodePackageTemperature(ULONG raw)
{
    LONG milli = (LONG)((raw >> 21) * 125);
    if ((raw & ZEN_TEMP_RANGE_SELECT) != 0 || (raw & (3u << 16)) == (3u << 16)) milli -= 49000;
    return milli;
}

static ULONG SelectCcdOffset(ULONG family, ULONG model)
{
    if (family == 0x19 && model >= 0x60 && model <= 0x7F) return ZEN_CCD_OFFSET_ZEN4;
    if (family == 0x19) return 0x300;
    if (family == 0x17 || family == 0x18) return 0x154;
    return 0;
}

static BOOLEAN ReadZenTemperature(UCHAR bus, UCHAR slot, PEXTERA_MONITOR_TELEMETRY result)
{
    ULONG raw = 0;
    if (!ReadSmn32(bus, slot, ZEN_SMN_TEMPERATURE, &raw)) return FALSE;
    result->RawRegister = raw;
    result->CelsiusMilli = DecodePackageTemperature(raw);
    result->PciBus = bus;
    result->PciSlot = slot;

    ULONG offset = SelectCcdOffset(result->CpuFamily, result->CpuModel);
    if (offset == 0) return TRUE;
    for (ULONG index = 0; index < EXTERA_MAX_CCDS; ++index) {
        ULONG ccdRaw = 0;
        if (!ReadSmn32(bus, slot, ZEN_CCD_TEMPERATURE_BASE + offset + (index * 4), &ccdRaw)) continue;
        if ((ccdRaw & ZEN_CCD_VALID) == 0) continue;
        result->CcdCelsiusMilli[result->CcdCount++] = (LONG)((ccdRaw & ZEN_CCD_MASK) * 125) - 49000;
    }
    return TRUE;
}

static NTSTATUS ReadTelemetry(PEXTERA_MONITOR_TELEMETRY result)
{
    result->Version = 1;
    result->DriverStatus = STATUS_NOT_SUPPORTED;
    ReadCpuIdentity(&result->CpuFamily, &result->CpuModel);
    if (result->CpuFamily != 0x19) return STATUS_SUCCESS;

    UCHAR bus = 0;
    UCHAR slot = 0;
    if (!FindAmdDataFabric(&bus, &slot)) {
        result->DriverStatus = STATUS_DEVICE_NOT_READY;
        return STATUS_SUCCESS;
    }

    ExAcquireFastMutex(&g_PciMutex);
    BOOLEAN temperatureRead = ReadZenTemperature(bus, slot, result);
    // Some AM5 firmware exposes the SMN window through D0F0 instead of the
    // discovered DF F3 function. Keep the validated legacy path as a
    // read-only fallback when the DF window returns an empty register.
    if (temperatureRead && result->RawRegister == 0) {
        result->CcdCount = 0;
        ULONG rootId = PciRead32(0, 0, 0);
        if ((rootId & 0xFFFF) == 0x1022)
            temperatureRead = ReadZenTemperature(0, 0, result);
    }
    if (temperatureRead) {
        ULONG rootId = PciRead32(0, 0, 0);
        if ((rootId & 0xFFFF) == 0x1022)
            (void)ReadRaphaelPmTable(0, 0, result);
    }
    ExReleaseFastMutex(&g_PciMutex);
    if (!temperatureRead) {
        result->DriverStatus = STATUS_IO_DEVICE_ERROR;
        return STATUS_SUCCESS;
    }

    ULONGLONG value = 0;
    if (ReadAllowedMsr(AMD_MSR_POWER_UNIT, &value)) result->PowerUnitRaw = (ULONG)value;
    if (ReadAllowedMsr(AMD_MSR_PACKAGE_ENERGY, &value)) result->PackageEnergy = (ULONG)value;
    if (ReadAllowedMsr(AMD_MSR_PSTATE_STATUS, &value)) result->PstateStatus = (ULONG)value;
    KeQueryPerformanceCounter(&result->QueryPerformanceFrequency);
    result->QueryPerformanceCounter = KeQueryPerformanceCounter(NULL);

    PROCESSOR_NUMBER currentProcessor = { 0 };
    KeGetCurrentProcessorNumberEx(&currentProcessor);
    ULONG processorCount = KeQueryActiveProcessorCountEx(currentProcessor.Group);
    if (processorCount > EXTERA_MAX_PROCESSORS) processorCount = EXTERA_MAX_PROCESSORS;
    result->ProcessorCount = processorCount;
    for (ULONG index = 0; index < processorCount; ++index) {
        KAFFINITY previous = KeSetSystemAffinityThreadEx(((KAFFINITY)1) << index);
        result->Cores[index].ProcessorIndex = index;
        ULONGLONG aperf = 0, mperf = 0, energy = 0;
        BOOLEAN okAperf = ReadAllowedMsr(AMD_MSR_APERF, &aperf);
        BOOLEAN okMperf = ReadAllowedMsr(AMD_MSR_MPERF, &mperf);
        BOOLEAN okEnergy = ReadAllowedMsr(AMD_MSR_CORE_ENERGY, &energy);
        BOOLEAN okPstate = ReadAllowedMsr(AMD_MSR_PSTATE_STATUS, &value);
        result->Cores[index].Aperf = aperf;
        result->Cores[index].Mperf = mperf;
        result->Cores[index].CoreEnergy = okEnergy ? (ULONG)energy : 0;
        result->Cores[index].Pstate = okPstate ? (ULONG)value : 0;
        result->Cores[index].Valid = okAperf && okMperf;
        KeRevertToUserAffinityThreadEx(previous);
    }
    result->Capabilities = 1u | (result->CcdCount != 0 ? 2u : 0) | (result->ProcessorCount != 0 ? 4u : 0);
    result->DriverStatus = STATUS_SUCCESS;
    return STATUS_SUCCESS;
}

static NTSTATUS ReadTemperature(PEXTERA_MONITOR_TEMPERATURE result)
{
    // Family 19h exposes the SMN index/data window on the AMD root function
    // D0F0. The DF F3 function identifies the node, but is not the SMN window.
    UCHAR bus = 0;
    UCHAR slot = 0;
    ULONG rootId = PciRead32(bus, slot, 0);
    if ((rootId & 0xFFFF) != 0x1022) {
        result->DriverStatus = STATUS_DEVICE_NOT_READY;
        return STATUS_SUCCESS;
    }
    result->PciBus = bus;
    result->PciSlot = slot;

    // AMD Family 19h reported temperature register, accessed through the DF SMN window.
    if (!PciWrite32(bus, slot, 0x60, 0x00059800)) {
        result->DriverStatus = STATUS_IO_DEVICE_ERROR;
        return STATUS_SUCCESS;
    }
    ULONG raw = PciRead32(bus, slot, 0x64);
    result->RawRegister = raw;
    // The main Tctl/Tdie field does not use the CCD VALID bit (bit 11).
    // That bit is only used when probing per-CCD registers.
    if (raw == 0xFFFFFFFF) {
        result->DriverStatus = STATUS_DEVICE_NOT_READY;
        return STATUS_SUCCESS;
    }

    LONG milli = (LONG)((raw >> 21) * 125);
    if ((raw & (1u << 19)) != 0 || (raw & (3u << 16)) == (3u << 16)) milli -= 49000;
    if (milli < -40000 || milli > 150000) {
        result->DriverStatus = STATUS_DATA_ERROR;
        return STATUS_SUCCESS;
    }

    result->CelsiusMilli = milli;
    result->RawRegister = raw;
    result->PciBus = bus;
    result->PciSlot = slot;
    result->DriverStatus = STATUS_SUCCESS;
    return STATUS_SUCCESS;
}

static NTSTATUS Complete(PIRP irp, NTSTATUS status, ULONG_PTR information)
{
    irp->IoStatus.Status = status;
    irp->IoStatus.Information = information;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

static NTSTATUS DispatchCreateClose(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    return Complete(irp, STATUS_SUCCESS, 0);
}

static NTSTATUS DispatchDeviceControl(PDEVICE_OBJECT device, PIRP irp)
{
    UNREFERENCED_PARAMETER(device);
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    ULONG code = stack->Parameters.DeviceIoControl.IoControlCode;
    if (code == IOCTL_EXTERA_GET_TELEMETRY) {
        if (stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(EXTERA_MONITOR_TELEMETRY))
            return Complete(irp, STATUS_BUFFER_TOO_SMALL, 0);
        PEXTERA_MONITOR_TELEMETRY result = (PEXTERA_MONITOR_TELEMETRY)irp->AssociatedIrp.SystemBuffer;
        RtlZeroMemory(result, sizeof(*result));
        NTSTATUS status = ReadTelemetry(result);
        return Complete(irp, status, sizeof(*result));
    }
    if ((code != IOCTL_EXTERA_GET_TEMPERATURE && code != IOCTL_EXTERA_GET_TEMPERATURE_ANY) ||
        stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(EXTERA_MONITOR_TEMPERATURE))
        return Complete(irp, STATUS_INVALID_DEVICE_REQUEST, 0);

    PEXTERA_MONITOR_TEMPERATURE result = (PEXTERA_MONITOR_TEMPERATURE)irp->AssociatedIrp.SystemBuffer;
    RtlZeroMemory(result, sizeof(*result));
    NTSTATUS status = ReadTemperature(result);
    return Complete(irp, status, sizeof(*result));
}

static VOID DriverUnload(PDRIVER_OBJECT driver)
{
    if (g_PmTableMapping != NULL) {
        MmUnmapIoSpace(g_PmTableMapping, PAGE_SIZE);
        g_PmTableMapping = NULL;
    }
    UNICODE_STRING dos = RTL_CONSTANT_STRING(DOS_NAME);
    IoDeleteSymbolicLink(&dos);
    if (driver->DeviceObject) IoDeleteDevice(driver->DeviceObject);
}

NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING path)
{
    UNREFERENCED_PARAMETER(path);
    UNICODE_STRING device = RTL_CONSTANT_STRING(DEVICE_NAME);
    UNICODE_STRING dos = RTL_CONSTANT_STRING(DOS_NAME);
    ExInitializeFastMutex(&g_PciMutex);
    NTSTATUS status = IoCreateDevice(driver, 0, &device, FILE_DEVICE_UNKNOWN, FILE_DEVICE_SECURE_OPEN, FALSE, &g_Device);
    if (!NT_SUCCESS(status)) return status;
    status = IoCreateSymbolicLink(&dos, &device);
    if (!NT_SUCCESS(status)) { IoDeleteDevice(g_Device); return status; }
    for (ULONG i = 0; i <= IRP_MJ_MAXIMUM_FUNCTION; ++i) driver->MajorFunction[i] = DispatchCreateClose;
    driver->MajorFunction[IRP_MJ_DEVICE_CONTROL] = DispatchDeviceControl;
    driver->DriverUnload = DriverUnload;
    g_Device->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}

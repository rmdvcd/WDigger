using Microsoft.Win32;

namespace WDigger.Core;

/// <summary>Fixed registry tweaks. Editable configuration never controls privileged registry paths.</summary>
public sealed class RegistryTweakService
{
    public sealed record Definition(string Id, string Name, string Key, string Value);
    public static IReadOnlyList<Definition> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new Definition("disable_telemetry", "Reducir telemetría (AllowTelemetry)", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
        new Definition("disable_cortana", "Desactivar Cortana por directiva", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana"),
        new Definition("disable_fast_startup", "Desactivar inicio rápido", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled")
    });

    private const string UndoRoot = @"SOFTWARE\WDigger\RegistryUndo";
    private readonly RegistryHive _hive;
    private readonly string _undoRoot;
    private readonly string _targetPrefix;

    public RegistryTweakService() : this(RegistryHive.LocalMachine, UndoRoot, "") { }

    // Test-only isolation: linked tests use a random HKCU subtree, never real policies.
    internal RegistryTweakService(RegistryHive hive, string undoRoot, string targetPrefix)
    {
        _hive = hive;
        _undoRoot = undoRoot;
        _targetPrefix = targetPrefix;
    }

    private string TargetKey(Definition item) => _targetPrefix + item.Key;

    public static Definition Resolve(string id) => Catalog.SingleOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException("Ajuste no autorizado.");

    public bool HasSnapshot(string id)
    {
        var item = Resolve(id);
        using var root = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        using var undo = root.OpenSubKey($"{_undoRoot}\\{item.Id}");
        return undo is not null;
    }

    public void Apply(string id)
    {
        var item = Resolve(id);
        using var guard = AcquireGuard();
        using var root = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        using var undoRoot = root.CreateSubKey(_undoRoot, writable: true)
            ?? throw new InvalidOperationException("No se puede abrir el almacén protegido de recuperación.");
        if (undoRoot.OpenSubKey(item.Id) is { } existing)
        {
            existing.Dispose();
            throw new InvalidOperationException("Ya existe una copia previa o una operación incompleta. Revísala antes de volver a aplicar.");
        }
        using var originalKey = root.OpenSubKey(TargetKey(item));
        var names = originalKey?.GetValueNames() ?? Array.Empty<string>();
        var hadValue = names.Contains(item.Value, StringComparer.OrdinalIgnoreCase);
        int previous = 0;
        if (hadValue)
        {
            if (originalKey!.GetValueKind(item.Value) != RegistryValueKind.DWord || originalKey.GetValue(item.Value) is not int value)
                throw new InvalidOperationException("El valor existente no es DWORD; no se modificará.");
            previous = value;
        }

        // Persist recovery before the first write to the target. An incomplete
        // snapshot remains visible and blocks reapplication after interruption.
        using var snapshot = undoRoot.CreateSubKey(item.Id, writable: true)
            ?? throw new InvalidOperationException("No se pudo crear la copia previa.");
        snapshot.SetValue("HadValue", hadValue ? 1 : 0, RegistryValueKind.DWord);
        snapshot.SetValue("Previous", previous, RegistryValueKind.DWord);
        snapshot.SetValue("Ready", 1, RegistryValueKind.DWord);
        snapshot.Flush();
        using var target = root.CreateSubKey(TargetKey(item), writable: true)
            ?? throw new InvalidOperationException("No se puede abrir la clave de destino; se conserva la copia previa.");
        var stillExists = target.GetValueNames().Contains(item.Value, StringComparer.OrdinalIgnoreCase);
        if (stillExists != hadValue || (hadValue && (target.GetValueKind(item.Value) != RegistryValueKind.DWord || target.GetValue(item.Value) is not int currentValue || currentValue != previous)))
            throw new InvalidOperationException("El valor cambió mientras se preparaba la copia; no se ha sobrescrito y la copia se conserva para revisión.");
        target.SetValue(item.Value, 0, RegistryValueKind.DWord);
        target.Flush();
        if (target.GetValueKind(item.Value) != RegistryValueKind.DWord || target.GetValue(item.Value) is not int current || current != 0)
            throw new InvalidOperationException("No se pudo verificar el cambio; se conserva la copia previa para revisión.");
    }

    public void Revert(string id)
    {
        var item = Resolve(id);
        using var guard = AcquireGuard();
        using var root = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        using var undoRoot = root.OpenSubKey(_undoRoot, writable: true)
            ?? throw new InvalidOperationException("No existe una copia previa para este ajuste.");
        using (var snapshot = undoRoot.OpenSubKey(item.Id))
        {
            if (snapshot is null || snapshot.GetValue("Ready") is not int ready || ready != 1 ||
                snapshot.GetValue("HadValue") is not int hadValue || hadValue is < 0 or > 1 ||
                snapshot.GetValue("Previous") is not int previous)
                throw new InvalidOperationException("La copia previa falta o está incompleta; no se revertirá automáticamente.");
            using var target = root.OpenSubKey(TargetKey(item), writable: true)
                ?? throw new InvalidOperationException("La clave de destino ya no existe; revisa la copia previa manualmente.");
            if (target.GetValue(item.Value) is not int current || target.GetValueKind(item.Value) != RegistryValueKind.DWord || current != 0)
                throw new InvalidOperationException("El valor cambió desde la aplicación; no se sobrescribirá. Revisa la copia previa manualmente.");
            if (hadValue == 1) target.SetValue(item.Value, previous, RegistryValueKind.DWord);
            else target.DeleteValue(item.Value, throwOnMissingValue: false);
            target.Flush();
            var restored = target.GetValueNames().Contains(item.Value, StringComparer.OrdinalIgnoreCase);
            if (restored != (hadValue == 1) || (restored && (target.GetValueKind(item.Value) != RegistryValueKind.DWord || target.GetValue(item.Value) is not int restoredValue || restoredValue != previous)))
                throw new InvalidOperationException("No se pudo verificar la reversión; se conserva la copia previa.");
        }
        undoRoot.DeleteSubKey(item.Id);
        undoRoot.Flush();
    }
    private static IDisposable AcquireGuard()
    {
        var mutex = new Mutex(false, @"Global\WDigger.RegistryTweaks");
        try
        {
            try { if (!mutex.WaitOne(0)) throw new InvalidOperationException("Otro ajuste está en curso."); }
            catch (AbandonedMutexException) { /* The previous attempt may have left a snapshot; it must be reviewed. */ }
            return new MutexGuard(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class MutexGuard(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

}

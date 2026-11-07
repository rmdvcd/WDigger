namespace WDigger.Core;

/// <summary>
/// Every functional module (hardware, repair, software, ...) implements this
/// interface. Modules are independent: if one throws, the shell can keep the
/// rest of the application running.
/// </summary>
public interface IModule
{
    string Name { get; }
    string Description { get; }
    void Run(AppHost host);
}

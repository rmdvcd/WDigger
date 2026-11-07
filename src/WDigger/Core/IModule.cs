using WDigger.UI;

namespace WDigger.Core;

/// <summary>
/// A native Windows module exposed by the shell as a set of asynchronous actions.
/// </summary>
public interface IModule
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<ModuleAction> CreateActions(AppHost host);
}

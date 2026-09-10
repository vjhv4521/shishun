using System.Collections;
using Haven.Framework.Core;

namespace Haven.Framework.Composition
{
    /// <summary>
    /// Installs an AOT service before the hot-update entry starts.
    /// </summary>
    public interface IFrameworkServiceInstaller
    {
        int Order { get; }
        IEnumerator Install(FrameworkContext context);
        void Uninstall();
    }
}

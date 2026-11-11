using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WDigger.Core;

/// <summary>
/// Verifies the actual process token, rather than group membership, before the
/// application performs any work. The application manifest is the primary
/// elevation mechanism; this is a fail-closed runtime guard for incorrectly
/// packaged or launched builds.
/// </summary>
public static class ElevationPolicy
{
    public const string RequiredExecutionLevel = "requireAdministrator";

    public static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var token = identity.AccessToken;
            return GetTokenInformation(
                token,
                TokenInformationClass.TokenElevation,
                out var elevation,
                Marshal.SizeOf<TokenElevation>(),
                out _)
                && elevation.TokenIsElevated != 0;
        }
        catch (Exception)
        {
            // A privilege-detection failure must never be interpreted as an
            // elevated process. The caller can show a useful startup error.
            return false;
        }
    }

    public static void RequireElevatedProcess()
    {
        if (!IsProcessElevated())
        {
            throw new InvalidOperationException(
                "WDigger debe iniciarse con permisos de administrador. Acepta el aviso de Control de cuentas de usuario y vuelve a intentarlo.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}

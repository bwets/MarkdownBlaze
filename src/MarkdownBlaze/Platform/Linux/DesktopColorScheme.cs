using System.Runtime.InteropServices;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Platform.Linux;

/// <summary>
/// Makes the window follow the desktop's light or dark preference on Linux.
/// </summary>
/// <remarks>
/// <para>
/// "Follow the system" is the page's <c>prefers-color-scheme</c>. WebKitGTK takes that from the GTK 3
/// theme, not from the desktop: GNOME's dark style (and KDE's, through the same portal) is a separate
/// <c>color-scheme</c> setting that GTK 3 never reads, so on a dark desktop with the default Adwaita theme
/// the page was told "light". This reads the setting from the freedesktop settings portal
/// (<c>org.freedesktop.appearance</c> / <c>color-scheme</c>: 1 prefers dark, 2 prefers light, 0 no
/// preference), hands it to GTK as <c>gtk-application-prefer-dark-theme</c>, which WebKitGTK and the
/// title bar both follow, and keeps doing so when the desktop switches while the app is open.
/// </para>
/// <para>
/// GLib, GIO and GTK are already in the process (PhotinoX's window is GTK 3), so they are called
/// directly rather than through a D-Bus package. Best-effort throughout: without a session bus or a
/// portal, nothing changes and the window keeps whatever GTK decided.
/// </para>
/// <para>
/// The libraries are looked for on the system's library path only, never next to the executable.
/// </para>
/// </remarks>
internal static unsafe partial class DesktopColorScheme
{
    private static readonly ILogger Logger = Log.For(nameof(DesktopColorScheme));

    private const string Gio = "libgio-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Gtk = "libgtk-3.so.0";
    private const string PortalName = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string SettingsInterface = "org.freedesktop.portal.Settings";
    private const string AppearanceNamespace = "org.freedesktop.appearance";
    private const string ColorSchemeKey = "color-scheme";
    private const string PreferDarkProperty = "gtk-application-prefer-dark-theme";

    /// <summary>G_BUS_TYPE_SESSION.</summary>
    private const int SessionBus = 2;

    /// <summary>G_TYPE_BOOLEAN: fundamental type 5, shifted as GLib numbers them.</summary>
    private const nuint BooleanType = 5 << 2;

    /// <summary>The portal's color-scheme value for "prefers dark".</summary>
    private const uint PreferDark = 1;
    private const int CallTimeoutMs = 1000;

    /// <summary>
    /// Reads the desktop's preference, applies it, and follows its changes. Call it on the GTK thread once
    /// the window exists (GTK is initialised then): the change signal is delivered on that thread's main
    /// loop, which is the one GTK runs.
    /// </summary>
    public static void Follow()
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var bus = g_bus_get_sync(SessionBus, 0, 0);
            if (bus == 0)
            {
                LogNoPreference(Logger);
                return;
            }

            // Subscribed before reading, so that a switch made in between is not lost. The connection is
            // kept for the life of the process, which is as long as the subscription is wanted.
            _ = g_dbus_connection_signal_subscribe(
                bus,
                PortalName,
                SettingsInterface,
                "SettingChanged",
                PortalPath,
                AppearanceNamespace,
                0,
                &OnSettingChanged,
                0,
                0);

            if (Read(bus) is { } scheme) Apply(scheme);
            else LogNoPreference(Logger);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            LogUnavailable(Logger, ex);
        }
    }

    /// <summary>The portal's <c>color-scheme</c> value, or null when there is no portal to ask.</summary>
    private static uint? Read(nint bus)
    {
        // Read (version 1 of the interface, which every portal has) answers (v) with the value wrapped in
        // one more variant; ReadOne (version 2) does not. SchemeIn unwraps either.
        var keys = stackalloc nint[2];
        keys[0] = g_variant_new_string(AppearanceNamespace);
        keys[1] = g_variant_new_string(ColorSchemeKey);
        var parameters = g_variant_new_tuple(keys, 2);

        var reply = g_dbus_connection_call_sync(
            bus, PortalName, PortalPath, SettingsInterface, "Read", parameters, 0, 0, CallTimeoutMs, 0, 0);
        if (reply == 0) return null;
        try
        {
            return SchemeIn(reply, 0);
        }
        finally
        {
            g_variant_unref(reply);
        }
    }

    /// <summary>
    /// The portal's SettingChanged(namespace, key, value) signal, already narrowed to the appearance
    /// namespace by the subscription. Runs on the GTK thread.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void OnSettingChanged(nint connection, nint sender, nint objectPath, nint interfaceName, nint signalName, nint parameters, nint userData)
    {
        try
        {
            var key = g_variant_get_child_value(parameters, 1);
            string? name;
            try
            {
                name = Marshal.PtrToStringUTF8(g_variant_get_string(key, 0));
            }
            finally
            {
                g_variant_unref(key);
            }

            if (string.Equals(name, ColorSchemeKey, StringComparison.Ordinal) && SchemeIn(parameters, 2) is { } scheme) Apply(scheme);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Decided: a catch-all, written with a filter so that CA1031 accepts it. This is called from
            // GLib's main loop, and an exception that left it would end the process; a missed switch of
            // the colour scheme is not worth that. The log gets the cause.
            LogChangeFailed(Logger, ex);
        }
    }

    /// <summary>The unsigned value at <paramref name="index"/> of a tuple, under however many variants it is wrapped in.</summary>
    private static uint? SchemeIn(nint tuple, nuint index)
    {
        var value = g_variant_get_child_value(tuple, index);
        try
        {
            while (string.Equals(Marshal.PtrToStringUTF8(g_variant_get_type_string(value)), "v", StringComparison.Ordinal))
            {
                var inner = g_variant_get_variant(value);
                g_variant_unref(value);
                value = inner;
            }

            return string.Equals(Marshal.PtrToStringUTF8(g_variant_get_type_string(value)), "u", StringComparison.Ordinal) ? g_variant_get_uint32(value) : null;
        }
        finally
        {
            g_variant_unref(value);
        }
    }

    /// <summary>Tells GTK, and through it WebKitGTK and the title bar, whether the desktop prefers dark.</summary>
    private static void Apply(uint scheme)
    {
        var settings = gtk_settings_get_default();
        if (settings == 0) return;

        var dark = scheme == PreferDark;
        var value = stackalloc long[3];   // a GValue: its type, then two words of data; zeroed, as g_value_init requires
        _ = g_value_init(value, BooleanType);
        g_value_set_boolean(value, dark ? 1 : 0);
        g_object_set_property(settings, PreferDarkProperty, value);
        g_value_unset(value);
        LogApplied(Logger, dark ? "dark" : "light");
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Gio)]
    private static partial nint g_bus_get_sync(int busType, nint cancellable, nint error);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Gio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_dbus_connection_call_sync(
        nint connection, string busName, string objectPath, string interfaceName, string methodName,
        nint parameters, nint replyType, int flags, int timeoutMsec, nint cancellable, nint error);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Gio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint g_dbus_connection_signal_subscribe(
        nint connection, string sender, string interfaceName, string member, string objectPath, string arg0,
        int flags, delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint userData, nint userDataFree);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_variant_new_string(string value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial nint g_variant_new_tuple(nint* children, nuint count);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial nint g_variant_get_child_value(nint value, nuint index);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial nint g_variant_get_variant(nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial nint g_variant_get_type_string(nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial nint g_variant_get_string(nint value, nint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial uint g_variant_get_uint32(nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GLib)]
    private static partial void g_variant_unref(nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Gtk)]
    private static partial nint gtk_settings_get_default();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GObject)]
    private static partial long* g_value_init(long* value, nuint type);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GObject)]
    private static partial void g_value_set_boolean(long* value, int boolean);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GObject)]
    private static partial void g_value_unset(long* value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void g_object_set_property(nint target, string propertyName, long* value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Following the desktop's colour scheme: {Scheme}")]
    private static partial void LogApplied(ILogger log, string scheme);

    [LoggerMessage(Level = LogLevel.Information, Message = "No desktop colour scheme to follow (no session bus or settings portal); GTK's own theme decides")]
    private static partial void LogNoPreference(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not follow the desktop's colour scheme: GLib or GTK is not available")]
    private static partial void LogUnavailable(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not apply a change of the desktop's colour scheme")]
    private static partial void LogChangeFailed(ILogger log, Exception ex);
}

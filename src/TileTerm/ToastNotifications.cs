using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace TileTerm;

/// <summary>
/// Shows Windows toast notifications (the ones that slide in at the bottom right and stay in the
/// notification center).
///
/// The toast API is a WinRT one. Using it the usual way from .NET means targeting a Windows SDK
/// version (<c>net9.0-windows10.0.xxxxx</c>), which ships a ~25 MB projection assembly with the app;
/// the handful of calls needed here are made directly through COM instead (<c>combase.dll</c> and the
/// interfaces' vtables), which costs nothing in size.
///
/// A desktop app that is not packaged needs an AppUserModelID that Windows knows to show toasts under.
/// It is registered per user in <c>HKCU\Software\Classes\AppUserModelId\</c><see cref="AppUserModelId"/>
/// (display name and icon) — no Start-menu shortcut required, so it works the same for the installed
/// app, the portable one and a development build.
/// </summary>
internal static unsafe class ToastNotifications
{
    public const string AppUserModelId = "syanmi.TileTerm";

    private static readonly Guid IID_IToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    private static readonly Guid IID_IToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid IID_IXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static readonly Guid IID_IXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");

    /// <summary>Toasts shown recently, kept alive so their "clicked" handler can still fire.</summary>
    private static readonly Queue<IntPtr> s_recent = new();
    private const int RecentLimit = 16;

    private static IntPtr s_notifier;
    private static bool s_unavailable;

    /// <summary>Shows a toast. <paramref name="onClicked"/> runs on a background thread when the user
    /// clicks it (while this process is still running). Returns false if toasts are not available here;
    /// it never throws.</summary>
    public static bool Show(string title, string body, Action onClicked)
    {
        if (s_unavailable) return false;
        try
        {
            if (s_notifier == IntPtr.Zero) s_notifier = CreateNotifier();
            ShowToast(title, body, onClicked);
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or SecurityException
                                   or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            // Pre-Windows 10, or a policy that blocks it. Not worth retrying for every notification.
            s_unavailable = true;
            return false;
        }
    }

    private static IntPtr CreateNotifier()
    {
        RegisterAppUserModelId();

        var statics = GetActivationFactory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
        try
        {
            using var id = new HString(AppUserModelId);
            IntPtr notifier;
            // IToastNotificationManagerStatics::CreateToastNotifierWithId
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(statics)[7])(statics, id.Handle, &notifier));
            return notifier;
        }
        finally { Marshal.Release(statics); }
    }

    private static void ShowToast(string title, string body, Action onClicked)
    {
        IntPtr xmlInspectable = 0, xmlIO = 0, xmlDocument = 0, factory = 0, toast = 0, handler = 0;
        try
        {
            using (var className = new HString("Windows.Data.Xml.Dom.XmlDocument"))
                Check(RoActivateInstance(className.Handle, out xmlInspectable));
            xmlIO = QueryInterface(xmlInspectable, IID_IXmlDocumentIO);
            using (var xml = new HString(BuildXml(title, body)))
                // IXmlDocumentIO::LoadXml
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VTable(xmlIO)[6])(xmlIO, xml.Handle));
            xmlDocument = QueryInterface(xmlInspectable, IID_IXmlDocument);

            factory = GetActivationFactory("Windows.UI.Notifications.ToastNotification", IID_IToastNotificationFactory);
            // IToastNotificationFactory::CreateToastNotification
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(factory)[6])(factory, xmlDocument, &toast));

            handler = ActivatedHandler.Create(onClicked);
            long token;
            // IToastNotification::add_Activated
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)VTable(toast)[11])(toast, handler, &token));

            // IToastNotifier::Show
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VTable(s_notifier)[6])(s_notifier, toast));

            s_recent.Enqueue(toast);
            toast = 0;   // owned by s_recent now
            while (s_recent.Count > RecentLimit) Marshal.Release(s_recent.Dequeue());
        }
        finally
        {
            foreach (var p in new[] { xmlInspectable, xmlIO, xmlDocument, factory, toast, handler })
                if (p != 0) Marshal.Release(p);
        }
    }

    private static string BuildXml(string title, string body) =>
        "<toast><visual><binding template=\"ToastGeneric\">" +
        $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
        "</binding></visual></toast>";

    /// <summary>Tells Windows the name and icon to show for <see cref="AppUserModelId"/>. The icon has to
    /// be a file, so the app's own image is copied next to the user data.</summary>
    private static void RegisterAppUserModelId()
    {
        var iconPath = Path.Combine(AppPaths.DataDirectory, "notification-icon.png");
        try
        {
            if (!File.Exists(iconPath) &&
                Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TileTerm-titlebar.png")) is { } resource)
            {
                using var source = resource.Stream;
                using var target = File.Create(iconPath);
                source.CopyTo(target);
            }
        }
        catch (IOException) { iconPath = ""; }
        catch (UnauthorizedAccessException) { iconPath = ""; }

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        key.SetValue("DisplayName", "TileTerm");
        if (iconPath.Length > 0) key.SetValue("IconUri", iconPath);
    }

    private static IntPtr GetActivationFactory(string className, Guid iid)
    {
        using var name = new HString(className);
        Check(RoGetActivationFactory(name.Handle, ref iid, out var factory));
        return factory;
    }

    private static IntPtr QueryInterface(IntPtr unknown, Guid iid)
    {
        Check(Marshal.QueryInterface(unknown, in iid, out var result));
        return result;
    }

    private static void** VTable(IntPtr comObject) => *(void***)comObject;

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private readonly ref struct HString
    {
        public IntPtr Handle { get; }

        public HString(string value)
        {
            Check(WindowsCreateString(value, value.Length, out var handle));
            Handle = handle;
        }

        public void Dispose() => WindowsDeleteString(Handle);
    }

    /// <summary>
    /// A minimal native COM object implementing <c>TypedEventHandler&lt;ToastNotification, object&gt;</c>
    /// (IUnknown + Invoke), for <c>IToastNotification::add_Activated</c>. It is agile (the event is raised
    /// on a thread-pool thread), and holds the managed callback through a GC handle that is freed with it.
    /// </summary>
    private static class ActivatedHandler
    {
        private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
        private static readonly Guid IID_ToastActivatedHandler = new("AB54DE2D-97D9-5528-B6AD-105AFE156530");
        private const int E_NOINTERFACE = unchecked((int)0x80004002);

        [StructLayout(LayoutKind.Sequential)]
        private struct Native
        {
            public IntPtr VTable;
            public IntPtr Callback;   // GCHandle of the Action
            public int RefCount;
        }

        private static readonly IntPtr s_vtable = CreateVTable();

        private static IntPtr CreateVTable()
        {
            var vtable = (IntPtr*)NativeMemory.Alloc((nuint)(4 * sizeof(IntPtr)));
            vtable[0] = (IntPtr)(delegate* unmanaged[Stdcall]<Native*, Guid*, IntPtr*, int>)&QueryInterface;
            vtable[1] = (IntPtr)(delegate* unmanaged[Stdcall]<Native*, uint>)&AddRef;
            vtable[2] = (IntPtr)(delegate* unmanaged[Stdcall]<Native*, uint>)&Release;
            vtable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<Native*, IntPtr, IntPtr, int>)&Invoke;
            return (IntPtr)vtable;
        }

        /// <summary>Returns the new object with one reference, which the caller releases.</summary>
        public static IntPtr Create(Action callback)
        {
            var self = (Native*)NativeMemory.Alloc((nuint)sizeof(Native));
            self->VTable = s_vtable;
            self->Callback = GCHandle.ToIntPtr(GCHandle.Alloc(callback));
            self->RefCount = 1;
            return (IntPtr)self;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int QueryInterface(Native* self, Guid* iid, IntPtr* result)
        {
            if (*iid == IID_IUnknown || *iid == IID_IAgileObject || *iid == IID_ToastActivatedHandler)
            {
                Interlocked.Increment(ref self->RefCount);
                *result = (IntPtr)self;
                return 0;
            }
            *result = IntPtr.Zero;
            return E_NOINTERFACE;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint AddRef(Native* self) => (uint)Interlocked.Increment(ref self->RefCount);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint Release(Native* self)
        {
            int count = Interlocked.Decrement(ref self->RefCount);
            if (count == 0)
            {
                GCHandle.FromIntPtr(self->Callback).Free();
                NativeMemory.Free(self);
            }
            return (uint)count;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int Invoke(Native* self, IntPtr sender, IntPtr args)
        {
            try
            {
                (GCHandle.FromIntPtr(self->Callback).Target as Action)?.Invoke();
            }
            catch (Exception)
            {
                // Never let an exception cross back into native code.
            }
            return 0;
        }
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);
}

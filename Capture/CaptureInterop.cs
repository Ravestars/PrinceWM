using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace PrinceWM.Capture;

internal static class CaptureInterop
{

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr itemPtr = interop.CreateForWindow(hwnd, ref iid);
        var item = GraphicsCaptureItem.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return item;
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevicePtr)
    {
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr graphicsDevice);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        var device = MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
        Marshal.Release(graphicsDevice);
        return device;
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private static readonly Guid ID3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static IntPtr GetTexturePointer(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2DIid;
        return access.GetInterface(ref iid);
    }
}

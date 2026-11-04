// Standalone prototype. Not registered as an MCP tool and never injected into CAD.
#include <windows.h>
#include <d3d11.h>
#include <dwmapi.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <filesystem>
#include <iostream>
#include <string>
#include <chrono>
#include <algorithm>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace winrt::Windows::Graphics::Imaging;
using namespace winrt::Windows::Storage;

uint64_t Number(const wchar_t* value) {
    std::wstring text(value);size_t count=0;
    if(text.empty()||text.find_first_not_of(L"0123456789")!=std::wstring::npos)throw std::invalid_argument("integer");
    const auto result=std::stoull(text,&count);
    if(count!=text.size())throw std::invalid_argument("integer");
    return result;
}
void CheckTarget(HWND window,DWORD pid,uint64_t ticks) {
    DWORD owner=0;GetWindowThreadProcessId(window,&owner);
    if(!IsWindow(window)||owner!=pid||!IsWindowVisible(window)||IsIconic(window)||!MonitorFromWindow(window,MONITOR_DEFAULTTONULL))throw std::runtime_error("window_unavailable");
    handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid));
    if(!process)throw std::runtime_error("process_unavailable");
    FILETIME start{},end{},kernel{},user{};
    check_bool(GetProcessTimes(process.get(),&start,&end,&kernel,&user));
    const uint64_t actual=(static_cast<uint64_t>(start.dwHighDateTime)<<32)|start.dwLowDateTime;
    if(actual+504911232000000000ULL!=ticks)throw std::runtime_error("process_identity_changed");
}

struct ViewportUnavailable : std::runtime_error {
    ViewportUnavailable() : std::runtime_error("viewport_unavailable") { }
};

struct ViewportTarget {
    HWND window;
    int width, height;
};

RECT ViewportInFrame(HWND window, DWORD pid, ViewportTarget viewport, int width, int height) {
    DWORD owner = 0;
    GetWindowThreadProcessId(viewport.window, &owner);
    RECT frame{}, client{};
    if (owner != pid || !IsChild(window, viewport.window) || !IsWindowVisible(viewport.window) ||
        FAILED(DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, &frame, sizeof(frame))) ||
        frame.right - frame.left != width || frame.bottom - frame.top != height ||
        !GetClientRect(viewport.window, &client)) throw ViewportUnavailable();

    POINT corners[]{{client.left, client.top}, {client.right, client.bottom}};
    SetLastError(ERROR_SUCCESS);
    if (!MapWindowPoints(viewport.window, nullptr, corners, 2) && GetLastError() != ERROR_SUCCESS)
        throw ViewportUnavailable();
    // DWM bounds and WGC frame size must describe the same physical rectangle.
    // An exact SCREENSIZE match prevents cropping a document container instead of a viewport.
    RECT crop{corners[0].x - frame.left, corners[0].y - frame.top,
        corners[1].x - frame.left, corners[1].y - frame.top};
    if (crop.left < 0 || crop.top < 0 || crop.right > width || crop.bottom > height ||
        crop.right - crop.left != viewport.width || crop.bottom - crop.top != viewport.height)
        throw ViewportUnavailable();
    return crop;
}

IDirect3DSurface CropSurface(const IDirect3DSurface& surface, const com_ptr<ID3D11Device>& device, const RECT& crop) {
    using Access = ::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;
    com_ptr<ID3D11Texture2D> source;
    check_hresult(surface.as<Access>()->GetInterface(__uuidof(ID3D11Texture2D), source.put_void()));
    D3D11_TEXTURE2D_DESC description{};
    source->GetDesc(&description);
    if (static_cast<UINT>(crop.right) > description.Width || static_cast<UINT>(crop.bottom) > description.Height)
        throw ViewportUnavailable();
    description.Width = static_cast<UINT>(crop.right - crop.left);
    description.Height = static_cast<UINT>(crop.bottom - crop.top);
    description.MipLevels = description.ArraySize = 1;
    description.Usage = D3D11_USAGE_DEFAULT;
    description.CPUAccessFlags = description.MiscFlags = 0;
    com_ptr<ID3D11Texture2D> cropped;
    check_hresult(device->CreateTexture2D(&description, nullptr, cropped.put()));
    com_ptr<ID3D11DeviceContext> context;
    device->GetImmediateContext(context.put());
    D3D11_BOX region{static_cast<UINT>(crop.left), static_cast<UINT>(crop.top), 0,
        static_cast<UINT>(crop.right), static_cast<UINT>(crop.bottom), 1};
    context->CopySubresourceRegion(cropped.get(), 0, 0, 0, 0, source.get(), 0, &region);
    winrt::Windows::Foundation::IInspectable result{nullptr};
    check_hresult(CreateDirect3D11SurfaceFromDXGISurface(cropped.as<IDXGISurface>().get(),
        reinterpret_cast<::IInspectable**>(put_abi(result))));
    return result.as<IDirect3DSurface>();
}

int wmain(int argc,wchar_t** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    if(argc==2&&std::wstring(argv[1])==L"--help") {
        std::cout<<"--capture <pid> <process_start_dotnet_ticks> <hwnd_decimal> <new_png_absolute_path> [--max-edge <pixels> [--viewport <hwnd> <width> <height>]]\n"
                    "One WGC window frame; no input, UIA, PrintWindow or CAD SDK. Caller must authorize and validate target/output and enforce timeout.\n";
        return 0;
    }
    try {
        if((argc!=6&&argc!=8&&argc!=12)||std::wstring(argv[1])!=L"--capture")throw std::invalid_argument("arguments");
        const auto maxEdge=argc>=8?Number(argv[7]):2048;
        if((argc>=8&&std::wstring(argv[6])!=L"--max-edge")||maxEdge<256||maxEdge>2048)throw std::invalid_argument("max_edge");
        const auto pid64=Number(argv[2]);if(pid64==0||pid64>MAXDWORD)throw std::invalid_argument("pid");
        const auto pid=static_cast<DWORD>(pid64);const auto ticks=Number(argv[3]);
        const auto window=reinterpret_cast<HWND>(static_cast<uintptr_t>(Number(argv[4])));
        ViewportTarget viewport{};
        if (argc == 12) {
            if (std::wstring(argv[8]) != L"--viewport") throw std::invalid_argument("viewport_argument");
            viewport.window = reinterpret_cast<HWND>(static_cast<uintptr_t>(Number(argv[9])));
            const auto width = Number(argv[10]);
            const auto height = Number(argv[11]);
            if (!viewport.window || width == 0 || height == 0 || width > INT_MAX || height > INT_MAX)
                throw ViewportUnavailable();
            viewport.width = static_cast<int>(width);
            viewport.height = static_cast<int>(height);
            if (!SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) throw ViewportUnavailable();
        }
        const std::filesystem::path output(argv[5]);
        if(!output.is_absolute()||output.extension()!=L".png"||std::filesystem::exists(output)||!std::filesystem::is_directory(output.parent_path()))
            throw std::invalid_argument("new_png_path_required");
        CheckTarget(window,pid,ticks);init_apartment(apartment_type::multi_threaded);
        if(!GraphicsCaptureSession::IsSupported())throw std::runtime_error("capture_unsupported");
        com_ptr<ID3D11Device> nativeDevice;
        check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,nativeDevice.put(),nullptr,nullptr));
        auto dxgi=nativeDevice.as<IDXGIDevice>();winrt::Windows::Foundation::IInspectable inspectable{nullptr};
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),reinterpret_cast<::IInspectable**>(put_abi(inspectable))));
        auto device=inspectable.as<IDirect3DDevice>();
        GraphicsCaptureItem item{nullptr};
        auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        check_hresult(interop->CreateForWindow(window,guid_of<GraphicsCaptureItem>(),put_abi(item)));
        const auto size=item.Size();
        if(size.Width<=0||size.Height<=0||static_cast<int64_t>(size.Width)*size.Height>16000000)throw std::runtime_error("frame_size_limit");
        RECT crop{};
        if (viewport.window) crop = ViewportInFrame(window, pid, viewport, size.Width, size.Height);
        auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(device,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
        handle signal(CreateEventW(nullptr,FALSE,FALSE,nullptr));check_bool(static_cast<bool>(signal));
        auto arrived=pool.FrameArrived(auto_revoke,[event=signal.get()](auto const&,auto const&){SetEvent(event);});
        auto session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled(false);
        LARGE_INTEGER frequency{},started{};QueryPerformanceFrequency(&frequency);QueryPerformanceCounter(&started);
        const auto startedTicks=static_cast<int64_t>(static_cast<long double>(started.QuadPart)*10000000/frequency.QuadPart);
        session.StartCapture();
        Direct3D11CaptureFrame frame{nullptr};
        const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
        while(!frame&&std::chrono::steady_clock::now()<deadline) {
            if(WaitForSingleObject(signal.get(),200)==WAIT_OBJECT_0) {
                frame=pool.TryGetNextFrame();
                if(frame&&frame.SystemRelativeTime().count()<startedTicks){frame.Close();frame=nullptr;}
            }
        }
        if(!frame)throw std::runtime_error("no_frame_timeout");
        const auto content=frame.ContentSize();
        if(content.Width!=size.Width||content.Height!=size.Height)throw std::runtime_error("window_resized_retry_required");
        const auto frameTime=frame.SystemRelativeTime().count();
        auto surface = frame.Surface();
        if (viewport.window) {
            auto after = ViewportInFrame(window, pid, viewport, content.Width, content.Height);
            if (!EqualRect(&crop, &after)) throw ViewportUnavailable();
            surface = CropSurface(surface, nativeDevice, crop);
        }
        auto bitmap=SoftwareBitmap::CreateCopyFromSurfaceAsync(surface).get();
        frame.Close();session.Close();arrived.revoke();pool.Close();
        CheckTarget(window,pid,ticks);
        auto folder=StorageFolder::GetFolderFromPathAsync(output.parent_path().wstring()).get();
        auto file=folder.CreateFileAsync(output.filename().wstring(),CreationCollisionOption::FailIfExists).get();
        auto stream=file.OpenAsync(FileAccessMode::ReadWrite).get();
        auto encoder=BitmapEncoder::CreateAsync(BitmapEncoder::PngEncoderId(),stream).get();
        const int sourceWidth = viewport.window ? viewport.width : content.Width;
        const int sourceHeight = viewport.window ? viewport.height : content.Height;
        const double scale=(std::min)(1.0,static_cast<double>(maxEdge)/(std::max)(sourceWidth,sourceHeight));
        const auto width=static_cast<uint32_t>((std::max)(1.0,sourceWidth*scale));
        const auto height=static_cast<uint32_t>((std::max)(1.0,sourceHeight*scale));
        encoder.BitmapTransform().ScaledWidth(width);encoder.BitmapTransform().ScaledHeight(height);
        encoder.SetSoftwareBitmap(bitmap);encoder.FlushAsync().get();stream.Close();bitmap.Close();
        CheckTarget(window,pid,ticks);
        std::cout<<"{\"captured\":true,\"source\":\"windows_graphics_capture\",\"document_consistency\":\"unverified\",\"width\":"<<width
                 <<",\"height\":"<<height<<",\"source_width\":"<<sourceWidth<<",\"source_height\":"<<sourceHeight
                 <<",\"viewport_only\":"<<(viewport.window ? "true" : "false")
                 <<",\"capture_started_system_relative_ticks\":\""<<startedTicks<<"\",\"frame_system_relative_ticks\":\""<<frameTime<<"\",\"pid\":"<<pid<<"}\n";
        return 0;
    } catch (ViewportUnavailable const&) {
        std::cout<<"{\"captured\":false,\"error\":\"viewport_unavailable\"}\n";return 4;
    } catch(hresult_error const& e) {
        std::cout<<"{\"captured\":false,\"error\":\"windows_capture_error\",\"hresult\":"<<static_cast<int32_t>(e.code())<<"}\n";return 3;
    } catch(std::exception const& e) {
        // All explicit messages are fixed identifiers; do not echo paths or window text.
        std::cerr<<"VisualWorker failed: "<<e.what()<<"\n";
        std::cout<<"{\"captured\":false,\"error\":\"capture_failed\"}\n";return 3;
    }
}

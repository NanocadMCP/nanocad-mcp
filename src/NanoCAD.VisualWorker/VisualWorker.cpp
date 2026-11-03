// Standalone prototype. Not registered as an MCP tool and never injected into CAD.
#include <windows.h>
#include <d3d11.h>
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
int wmain(int argc,wchar_t** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    if(argc==2&&std::wstring(argv[1])==L"--help") {
        std::cout<<"Prototype: --capture <pid> <process_start_dotnet_ticks> <hwnd_decimal> <new_png_absolute_path>\n"
                    "One WGC window frame; no input, UIA, PrintWindow or CAD SDK. Caller must authorize and validate target/output and enforce timeout.\n";
        return 0;
    }
    try {
        if((argc!=6&&argc!=8)||std::wstring(argv[1])!=L"--capture")throw std::invalid_argument("arguments");
        const auto maxEdge=argc==8?Number(argv[7]):2048;
        if(argc==8&&std::wstring(argv[6])!=L"--max-edge"||maxEdge<256||maxEdge>2048)throw std::invalid_argument("max_edge");
        const auto pid64=Number(argv[2]);if(pid64==0||pid64>MAXDWORD)throw std::invalid_argument("pid");
        const auto pid=static_cast<DWORD>(pid64);const auto ticks=Number(argv[3]);
        const auto window=reinterpret_cast<HWND>(static_cast<uintptr_t>(Number(argv[4])));
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
        auto bitmap=SoftwareBitmap::CreateCopyFromSurfaceAsync(frame.Surface()).get();
        frame.Close();session.Close();arrived.revoke();pool.Close();
        CheckTarget(window,pid,ticks);
        auto folder=StorageFolder::GetFolderFromPathAsync(output.parent_path().wstring()).get();
        auto file=folder.CreateFileAsync(output.filename().wstring(),CreationCollisionOption::FailIfExists).get();
        auto stream=file.OpenAsync(FileAccessMode::ReadWrite).get();
        auto encoder=BitmapEncoder::CreateAsync(BitmapEncoder::PngEncoderId(),stream).get();
        const double scale=(std::min)(1.0,static_cast<double>(maxEdge)/(std::max)(content.Width,content.Height));
        const auto width=static_cast<uint32_t>((std::max)(1.0,content.Width*scale));
        const auto height=static_cast<uint32_t>((std::max)(1.0,content.Height*scale));
        encoder.BitmapTransform().ScaledWidth(width);encoder.BitmapTransform().ScaledHeight(height);
        encoder.SetSoftwareBitmap(bitmap);encoder.FlushAsync().get();stream.Close();bitmap.Close();
        CheckTarget(window,pid,ticks);
        std::cout<<"{\"captured\":true,\"source\":\"windows_graphics_capture\",\"document_consistency\":\"unverified\",\"width\":"<<width
                 <<",\"height\":"<<height<<",\"source_width\":"<<content.Width<<",\"source_height\":"<<content.Height
                 <<",\"capture_started_system_relative_ticks\":\""<<startedTicks<<"\",\"frame_system_relative_ticks\":\""<<frameTime<<"\",\"pid\":"<<pid<<"}\n";
        return 0;
    } catch(hresult_error const& e) {
        std::cout<<"{\"captured\":false,\"error\":\"windows_capture_error\",\"hresult\":"<<static_cast<int32_t>(e.code())<<"}\n";return 3;
    } catch(std::exception const& e) {
        // All explicit messages are fixed identifiers; do not echo paths or window text.
        std::cerr<<"VisualWorker failed: "<<e.what()<<"\n";
        std::cout<<"{\"captured\":false,\"error\":\"capture_failed\"}\n";return 3;
    }
}

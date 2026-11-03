#include <windows.h>
#include <fstream>
#include <filesystem>
#include <cstdint>

LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);
        const COLORREF colors[]={RGB(220,40,40),RGB(40,180,70),RGB(40,80,220),RGB(245,210,30)};
        for(int i=0;i<4;i++) {
            RECT cell{(i%2)*320,(i/2)*180,(i%2+1)*320,(i/2+1)*180};
            auto brush=CreateSolidBrush(colors[i]);FillRect(dc,&cell,brush);DeleteObject(brush);
        }
        SetTextColor(dc,RGB(255,255,255));SetBkColor(dc,RGB(0,0,0));
        const wchar_t* label=L"VISUAL ONLY 7319";TextOutW(dc,220,165,label,16);
        EndPaint(window,&paint);return 0;
    }
    if(message==WM_TIMER){DestroyWindow(window);return 0;}
    if(message==WM_DESTROY){PostQuitMessage(0);return 0;}
    return DefWindowProcW(window,message,w,l);
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=2||std::filesystem::exists(argv[1]))return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW type{};type.lpfnWndProc=WindowProc;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"NanoCADMcpOwnCaptureFixture";
    if(!RegisterClassW(&type))return 3;
    // Own window on a real monitor, below other windows, never activated.
    // The first off-desktop fixture produced a black frame and is retained as failed evidence.
    const int x=GetSystemMetrics(SM_XVIRTUALSCREEN)+64,y=GetSystemMetrics(SM_YVIRTUALSCREEN)+64;
    auto window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,type.lpszClassName,L"MCP capture fixture",WS_POPUP,x,y,640,360,nullptr,nullptr,type.hInstance,nullptr);
    if(!window)return 4;
    SetWindowPos(window,HWND_BOTTOM,x,y,640,360,SWP_NOACTIVATE|SWP_SHOWWINDOW);UpdateWindow(window);SetTimer(window,1,15000,nullptr);
    {std::ofstream file{std::filesystem::path(argv[1])};file<<"{\"pid\":"<<GetCurrentProcessId()<<",\"hwnd\":\""<<reinterpret_cast<uintptr_t>(window)<<"\"}";}
    MSG message{};while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}
    return 0;
}

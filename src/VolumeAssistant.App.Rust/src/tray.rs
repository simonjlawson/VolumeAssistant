#![allow(non_snake_case)]

use std::sync::{Arc, Mutex};
use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM, POINT};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyMenu,
    DispatchMessageW, GetCursorPos, GetMessageW, MSG, PostQuitMessage,
    RegisterClassW, SetForegroundWindow, ShowWindow, TrackPopupMenu,
    TranslateMessage, WNDCLASSW,
    HWND_MESSAGE, MF_SEPARATOR, MF_STRING,
    SW_SHOW, SW_HIDE, TPM_BOTTOMALIGN, TPM_RIGHTBUTTON, TPM_RIGHTALIGN,
    WM_CLOSE, WM_COMMAND, WM_LBUTTONDBLCLK, WM_RBUTTONUP, WM_USER,
};
use windows_sys::Win32::UI::Shell::{
    Shell_NotifyIconW, NOTIFYICONDATAW, NOTIFYICONDATAW_0,
    NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE,
};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;

use crate::audio::AudioController;
use crate::coordinator::AppState;

pub const WM_TRAYICON: u32 = WM_USER + 1;
const IDM_OPEN: usize = 1001;
const IDM_EXIT: usize = 1002;

pub static mut MAIN_WINDOW: HWND = std::ptr::null_mut();
static mut AUDIO_PTR: *const Mutex<AudioController> = std::ptr::null();
static mut STATE_PTR: *const Mutex<AppState> = std::ptr::null();

pub fn run_tray(
    audio: Arc<Mutex<AudioController>>,
    state: Arc<Mutex<AppState>>,
) {
    unsafe {
        let audio_leaked = Arc::into_raw(audio);
        let state_leaked = Arc::into_raw(state);
        AUDIO_PTR = audio_leaked;
        STATE_PTR = state_leaked;

        let hinstance = GetModuleHandleW(std::ptr::null());

        let class_name: Vec<u16> = "VolumeAssistantTray\0".encode_utf16().collect();
        let wc = WNDCLASSW {
            style: 0,
            lpfnWndProc: Some(tray_wndproc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: hinstance,
            hIcon: std::ptr::null_mut(),
            hCursor: std::ptr::null_mut(),
            hbrBackground: std::ptr::null_mut(),
            lpszMenuName: std::ptr::null(),
            lpszClassName: class_name.as_ptr(),
        };
        RegisterClassW(&wc);

        let hwnd = CreateWindowExW(
            0,
            class_name.as_ptr(),
            std::ptr::null(),
            0,
            0, 0, 0, 0,
            HWND_MESSAGE,
            std::ptr::null_mut(),
            hinstance,
            std::ptr::null(),
        );

        if hwnd.is_null() {
            return;
        }

        let (vol_pct, muted) = {
            let audio = &*AUDIO_PTR;
            let guard = audio.lock().unwrap_or_else(|e| e.into_inner());
            (guard.get_volume_percent(), guard.get_muted())
        };

        let mut tooltip_arr = [0u16; 128];
        let tip_text: Vec<u16> = "VolumeAssistant\0".encode_utf16().collect();
        let len = tip_text.len().min(127);
        tooltip_arr[..len].copy_from_slice(&tip_text[..len]);

        let icon = crate::icon::create_volume_icon(vol_pct, muted);

        let nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: hwnd,
            uID: 1,
            uFlags: NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage: WM_TRAYICON,
            hIcon: icon,
            szTip: tooltip_arr,
            dwState: 0,
            dwStateMask: 0,
            szInfo: [0u16; 256],
            Anonymous: NOTIFYICONDATAW_0 { uTimeout: 0 },
            szInfoTitle: [0u16; 64],
            dwInfoFlags: 0,
            guidItem: windows_sys::core::GUID {
                data1: 0,
                data2: 0,
                data3: 0,
                data4: [0; 8],
            },
            hBalloonIcon: std::ptr::null_mut(),
        };

        Shell_NotifyIconW(NIM_ADD, &nid);

        let mut msg = MSG {
            hwnd: std::ptr::null_mut(),
            message: 0,
            wParam: 0,
            lParam: 0,
            time: 0,
            pt: POINT { x: 0, y: 0 },
        };

        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) != 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        Shell_NotifyIconW(NIM_DELETE, &nid);
    }
}

unsafe extern "system" fn tray_wndproc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_TRAYICON => {
            let event = (lparam & 0xFFFF) as u32;
            match event {
                WM_RBUTTONUP => {
                    show_context_menu(hwnd);
                }
                WM_LBUTTONDBLCLK => {
                    show_main_window(hwnd);
                }
                _ => {}
            }
            0
        }
        WM_COMMAND => {
            let id = (wparam & 0xFFFF) as usize;
            match id {
                IDM_OPEN => show_main_window(hwnd),
                IDM_EXIT => {
                    PostQuitMessage(0);
                }
                _ => {}
            }
            0
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe fn show_context_menu(hwnd: HWND) {
    let hmenu = CreatePopupMenu();

    let open_str: Vec<u16> = "Open\0".encode_utf16().collect();
    let exit_str: Vec<u16> = "Exit\0".encode_utf16().collect();

    AppendMenuW(hmenu, MF_STRING, IDM_OPEN, open_str.as_ptr());
    AppendMenuW(hmenu, MF_SEPARATOR, 0, std::ptr::null());
    AppendMenuW(hmenu, MF_STRING, IDM_EXIT, exit_str.as_ptr());

    let mut pt = POINT { x: 0, y: 0 };
    GetCursorPos(&mut pt);

    SetForegroundWindow(hwnd);
    TrackPopupMenu(
        hmenu,
        TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RIGHTALIGN,
        pt.x,
        pt.y,
        0,
        hwnd,
        std::ptr::null(),
    );

    DestroyMenu(hmenu);
}

unsafe fn show_main_window(tray_hwnd: HWND) {
    if !MAIN_WINDOW.is_null() {
        ShowWindow(MAIN_WINDOW, SW_SHOW);
        SetForegroundWindow(MAIN_WINDOW);
        return;
    }

    let hinstance = GetModuleHandleW(std::ptr::null());
    let audio = &*AUDIO_PTR;
    let state = &*STATE_PTR;
    crate::window::create_main_window(hinstance, audio, state);
}

#![allow(non_snake_case, dead_code)]

use std::sync::Mutex;
use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM, HMODULE};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, GetDlgItem, KillTimer, RegisterClassW,
    SendMessageW, SetTimer, SetWindowTextW, ShowWindow, WNDCLASSW,
    CS_HREDRAW, CS_VREDRAW, CW_USEDEFAULT, IDC_ARROW, IDI_APPLICATION,
    LB_ADDSTRING, LB_GETCOUNT, LB_SETTOPINDEX, LBS_NOTIFY,
    LoadCursorW, LoadIconW, SW_HIDE,
    WM_CLOSE, WM_CREATE, WM_DESTROY, WM_TIMER,
    WS_CHILD, WS_HSCROLL, WS_OVERLAPPEDWINDOW, WS_VISIBLE, WS_VSCROLL,
    WS_EX_CLIENTEDGE,
    BS_PUSHBUTTON,
};
use windows_sys::Win32::UI::Controls::{TCM_INSERTITEMW, TCIF_TEXT, TCS_TABS, TCITEMW};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;

use crate::audio::AudioController;
use crate::coordinator::AppState;

const IDC_TAB: i32 = 1000;
// Home tab controls (1100 series)
const IDC_HOME_STATUS_LABEL: i32 = 1100;
const IDC_HOME_TIMESTAMP_LABEL: i32 = 1101;
const IDC_HOME_DEVICE_NAME_LABEL: i32 = 1102;
const IDC_HOME_DEVICE_MODEL_LABEL: i32 = 1103;
const IDC_HOME_WIN_VOL_LABEL: i32 = 1104;
const IDC_HOME_WIN_MUTED_LABEL: i32 = 1105;
const IDC_HOME_CA_VOL_LABEL: i32 = 1106;
const IDC_HOME_CA_SOURCE_LABEL: i32 = 1107;
const IDC_HOME_CA_OUTPUT_LABEL: i32 = 1108;
const IDC_HOME_CA_POWER_LABEL: i32 = 1109;
// Connection tab controls (1200 series)
const IDC_STATUS_LABEL: i32 = 1200;
const IDC_WIN_VOL_LABEL: i32 = 1201;
const IDC_LOG_LIST: i32 = 1300;
const IDC_BTN_CONNECT: i32 = 1400;
const TIMER_REFRESH: usize = 1;

// SAFETY: All globals below are written once during WM_CREATE (before any other
// window message can arrive) and read only from the single Win32 message-loop thread.
static mut WIN_AUDIO_PTR: *const Mutex<AudioController> = std::ptr::null();
static mut WIN_STATE_PTR: *const Mutex<AppState> = std::ptr::null();
static mut TAB_HWND: HWND = std::ptr::null_mut();
static mut LOG_LIST_HWND: HWND = std::ptr::null_mut();
// Home tab control handles
static mut HOME_STATUS_HWND: HWND = std::ptr::null_mut();
static mut HOME_TIMESTAMP_HWND: HWND = std::ptr::null_mut();
static mut HOME_DEVICE_NAME_HWND: HWND = std::ptr::null_mut();
static mut HOME_DEVICE_MODEL_HWND: HWND = std::ptr::null_mut();
static mut HOME_WIN_VOL_HWND: HWND = std::ptr::null_mut();
static mut HOME_WIN_MUTED_HWND: HWND = std::ptr::null_mut();
static mut HOME_CA_VOL_HWND: HWND = std::ptr::null_mut();
static mut HOME_CA_SOURCE_HWND: HWND = std::ptr::null_mut();
static mut HOME_CA_OUTPUT_HWND: HWND = std::ptr::null_mut();
static mut HOME_CA_POWER_HWND: HWND = std::ptr::null_mut();
// Connection tab control handles
static mut STATUS_LABEL_HWND: HWND = std::ptr::null_mut();
static mut WIN_VOL_LABEL_HWND: HWND = std::ptr::null_mut();

pub unsafe fn create_main_window(
    hinstance: HMODULE,
    audio: *const Mutex<AudioController>,
    state: *const Mutex<AppState>,
) -> HWND {
    WIN_AUDIO_PTR = audio;
    WIN_STATE_PTR = state;

    let class_name: Vec<u16> = "VolumeAssistantMain\0".encode_utf16().collect();
    let wc = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW,
        lpfnWndProc: Some(main_wndproc),
        cbClsExtra: 0,
        cbWndExtra: 0,
        hInstance: hinstance,
        hIcon: LoadIconW(std::ptr::null_mut(), IDI_APPLICATION),
        hCursor: LoadCursorW(std::ptr::null_mut(), IDC_ARROW),
        hbrBackground: 6usize as *mut std::ffi::c_void,
        lpszMenuName: std::ptr::null(),
        lpszClassName: class_name.as_ptr(),
    };
    RegisterClassW(&wc);

    let title: Vec<u16> = "VolumeAssistant\0".encode_utf16().collect();
    let hwnd = CreateWindowExW(
        0,
        class_name.as_ptr(),
        title.as_ptr(),
        WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT, 600, 500,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        hinstance,
        std::ptr::null(),
    );

    crate::tray::MAIN_WINDOW = hwnd;

    hwnd
}

unsafe extern "system" fn main_wndproc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_CREATE => {
            create_controls(hwnd);
            SetTimer(hwnd, TIMER_REFRESH, 2000, None);
            0
        }
        WM_TIMER => {
            if wparam == TIMER_REFRESH {
                refresh_ui(hwnd);
            }
            0
        }
        WM_CLOSE => {
            ShowWindow(hwnd, SW_HIDE);
            crate::tray::MAIN_WINDOW = std::ptr::null_mut();
            0
        }
        WM_DESTROY => {
            KillTimer(hwnd, TIMER_REFRESH);
            crate::tray::MAIN_WINDOW = std::ptr::null_mut();
            0
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe fn create_controls(hwnd: HWND) {
    let hinstance = GetModuleHandleW(std::ptr::null());

    let tab_class: Vec<u16> = "SysTabControl32\0".encode_utf16().collect();
    let tab_hwnd = CreateWindowExW(
        0,
        tab_class.as_ptr(),
        std::ptr::null(),
        WS_CHILD | WS_VISIBLE | TCS_TABS,
        0, 0, 580, 460,
        hwnd,
        IDC_TAB as _,
        hinstance,
        std::ptr::null(),
    );
    TAB_HWND = tab_hwnd;

    add_tab(tab_hwnd, 0, "Home");
    add_tab(tab_hwnd, 1, "Connection");
    add_tab(tab_hwnd, 2, "Configuration");
    add_tab(tab_hwnd, 3, "Logs");

    // Home tab controls - left panel (connection status)
    create_label(hwnd, hinstance, IDC_HOME_STATUS_LABEL, "Status: Not connected", 20, 60, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_TIMESTAMP_LABEL, "Last update: —", 20, 85, 300, 20);
    
    // Home tab controls - device info
    create_label(hwnd, hinstance, IDC_HOME_DEVICE_NAME_LABEL, "Device: —", 20, 115, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_DEVICE_MODEL_LABEL, "Model: —", 20, 140, 300, 20);

    // Home tab controls - Windows audio metrics
    create_label(hwnd, hinstance, IDC_HOME_WIN_VOL_LABEL, "Windows Volume: —", 20, 170, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_WIN_MUTED_LABEL, "Windows Muted: —", 20, 195, 300, 20);

    // Home tab controls - Cambridge Audio metrics
    create_label(hwnd, hinstance, IDC_HOME_CA_VOL_LABEL, "CA Volume: —", 20, 225, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_CA_SOURCE_LABEL, "CA Source: —", 20, 250, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_CA_OUTPUT_LABEL, "CA Output: —", 20, 275, 300, 20);
    create_label(hwnd, hinstance, IDC_HOME_CA_POWER_LABEL, "CA Power: —", 20, 300, 300, 20);

    // Connection tab controls
    create_label(hwnd, hinstance, IDC_STATUS_LABEL, "Status: Not connected", 20, 60, 300, 20);
    create_label(hwnd, hinstance, IDC_WIN_VOL_LABEL, "Windows Volume: --", 20, 90, 300, 20);

    // Get handles for Home tab controls
    HOME_STATUS_HWND = GetDlgItem(hwnd, IDC_HOME_STATUS_LABEL);
    HOME_TIMESTAMP_HWND = GetDlgItem(hwnd, IDC_HOME_TIMESTAMP_LABEL);
    HOME_DEVICE_NAME_HWND = GetDlgItem(hwnd, IDC_HOME_DEVICE_NAME_LABEL);
    HOME_DEVICE_MODEL_HWND = GetDlgItem(hwnd, IDC_HOME_DEVICE_MODEL_LABEL);
    HOME_WIN_VOL_HWND = GetDlgItem(hwnd, IDC_HOME_WIN_VOL_LABEL);
    HOME_WIN_MUTED_HWND = GetDlgItem(hwnd, IDC_HOME_WIN_MUTED_LABEL);
    HOME_CA_VOL_HWND = GetDlgItem(hwnd, IDC_HOME_CA_VOL_LABEL);
    HOME_CA_SOURCE_HWND = GetDlgItem(hwnd, IDC_HOME_CA_SOURCE_LABEL);
    HOME_CA_OUTPUT_HWND = GetDlgItem(hwnd, IDC_HOME_CA_OUTPUT_LABEL);
    HOME_CA_POWER_HWND = GetDlgItem(hwnd, IDC_HOME_CA_POWER_LABEL);

    // Connection tab control handles
    STATUS_LABEL_HWND = GetDlgItem(hwnd, IDC_STATUS_LABEL);
    WIN_VOL_LABEL_HWND = GetDlgItem(hwnd, IDC_WIN_VOL_LABEL);

    let button_class: Vec<u16> = "BUTTON\0".encode_utf16().collect();
    let btn_text: Vec<u16> = "Connect\0".encode_utf16().collect();
    CreateWindowExW(
        0,
        button_class.as_ptr(),
        btn_text.as_ptr(),
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON as u32,
        20, 200, 100, 30,
        hwnd,
        IDC_BTN_CONNECT as _,
        hinstance,
        std::ptr::null(),
    );

    let list_class: Vec<u16> = "LISTBOX\0".encode_utf16().collect();
    let log_hwnd = CreateWindowExW(
        WS_EX_CLIENTEDGE,
        list_class.as_ptr(),
        std::ptr::null(),
        WS_CHILD | LBS_NOTIFY as u32 | WS_VSCROLL | WS_HSCROLL,
        0, 60, 580, 390,
        hwnd,
        IDC_LOG_LIST as _,
        hinstance,
        std::ptr::null(),
    );
    LOG_LIST_HWND = log_hwnd;
}

unsafe fn add_tab(tab_hwnd: HWND, index: i32, label: &str) {
    let text: Vec<u16> = format!("{}\0", label).encode_utf16().collect();
    let mut item = TCITEMW {
        mask: TCIF_TEXT,
        dwState: 0,
        dwStateMask: 0,
        pszText: text.as_ptr() as *mut u16,
        cchTextMax: text.len() as i32,
        iImage: -1,
        lParam: 0,
    };
    SendMessageW(
        tab_hwnd,
        TCM_INSERTITEMW,
        index as usize,
        &mut item as *mut _ as LPARAM,
    );
}

unsafe fn create_label(
    parent: HWND,
    hinstance: HMODULE,
    id: i32,
    text: &str,
    x: i32,
    y: i32,
    w: i32,
    h: i32,
) {
    let class: Vec<u16> = "STATIC\0".encode_utf16().collect();
    let label_text: Vec<u16> = format!("{}\0", text).encode_utf16().collect();
    CreateWindowExW(
        0,
        class.as_ptr(),
        label_text.as_ptr(),
        WS_CHILD | WS_VISIBLE | 0u32, // SS_LEFT = 0
        x, y, w, h,
        parent,
        id as _,
        hinstance,
        std::ptr::null(),
    );
}

unsafe fn refresh_ui(_hwnd: HWND) {
    if WIN_AUDIO_PTR.is_null() || WIN_STATE_PTR.is_null() {
        return;
    }

    let (vol_pct, muted) = {
        let audio = &*WIN_AUDIO_PTR;
        if let Ok(guard) = audio.lock() {
            (guard.get_volume_percent(), guard.get_muted())
        } else {
            return;
        }
    };

    let (ca_connected, ca_device, ca_model, ca_vol, ca_source, ca_power, log_entries) = {
        let state = &*WIN_STATE_PTR;
        if let Ok(guard) = state.lock() {
            (
                guard.cambridge.connected,
                guard.cambridge.device_name.clone(),
                guard.cambridge.model.clone(),
                guard.cambridge.volume_percent,
                guard.cambridge.source.clone(),
                guard.cambridge.power,
                guard.log_entries.clone(),
            )
        } else {
            return;
        }
    };

    let now = chrono::Local::now();
    let timestamp = now.format("%y/%m/%d %H:%M").to_string();

    // Update Home tab
    if !HOME_STATUS_HWND.is_null() {
        let status = if ca_connected {
            format!("Status: Connected\0")
        } else {
            "Status: Disconnected\0".to_string()
        };
        let status_w: Vec<u16> = status.encode_utf16().collect();
        SetWindowTextW(HOME_STATUS_HWND, status_w.as_ptr());
    }

    if !HOME_TIMESTAMP_HWND.is_null() {
        let ts_text = format!("Last update: {}\0", timestamp);
        let ts_w: Vec<u16> = ts_text.encode_utf16().collect();
        SetWindowTextW(HOME_TIMESTAMP_HWND, ts_w.as_ptr());
    }

    if !HOME_DEVICE_NAME_HWND.is_null() {
        let device_text = if ca_connected && !ca_device.is_empty() {
            format!("Device: {}\0", ca_device)
        } else {
            "Device: —\0".to_string()
        };
        let device_w: Vec<u16> = device_text.encode_utf16().collect();
        SetWindowTextW(HOME_DEVICE_NAME_HWND, device_w.as_ptr());
    }

    if !HOME_DEVICE_MODEL_HWND.is_null() {
        let model_text = if ca_connected && !ca_model.is_empty() {
            format!("Model: {}\0", ca_model)
        } else {
            "Model: —\0".to_string()
        };
        let model_w: Vec<u16> = model_text.encode_utf16().collect();
        SetWindowTextW(HOME_DEVICE_MODEL_HWND, model_w.as_ptr());
    }

    if !HOME_WIN_VOL_HWND.is_null() {
        let vol_text = if muted {
            format!("Windows Volume: {:.0}% (Muted)\0", vol_pct)
        } else {
            format!("Windows Volume: {:.0}%\0", vol_pct)
        };
        let vol_w: Vec<u16> = vol_text.encode_utf16().collect();
        SetWindowTextW(HOME_WIN_VOL_HWND, vol_w.as_ptr());
    }

    if !HOME_WIN_MUTED_HWND.is_null() {
        let muted_text = if muted { "Windows Muted: Yes\0" } else { "Windows Muted: No\0" };
        let muted_w: Vec<u16> = muted_text.encode_utf16().collect();
        SetWindowTextW(HOME_WIN_MUTED_HWND, muted_w.as_ptr());
    }

    if !HOME_CA_VOL_HWND.is_null() {
        let ca_vol_text = if ca_connected {
            if let Some(vol) = ca_vol {
                format!("CA Volume: {}\0", vol)
            } else {
                "CA Volume: —\0".to_string()
            }
        } else {
            "CA Volume: —\0".to_string()
        };
        let ca_vol_w: Vec<u16> = ca_vol_text.encode_utf16().collect();
        SetWindowTextW(HOME_CA_VOL_HWND, ca_vol_w.as_ptr());
    }

    if !HOME_CA_SOURCE_HWND.is_null() {
        let ca_source_text = if ca_connected && !ca_source.is_empty() {
            format!("CA Source: {}\0", ca_source)
        } else {
            "CA Source: —\0".to_string()
        };
        let ca_source_w: Vec<u16> = ca_source_text.encode_utf16().collect();
        SetWindowTextW(HOME_CA_SOURCE_HWND, ca_source_w.as_ptr());
    }

    if !HOME_CA_OUTPUT_HWND.is_null() {
        // Output field not available in current schema, show as info
        let ca_output_text = if ca_connected {
            "CA Output: —\0".to_string()
        } else {
            "CA Output: —\0".to_string()
        };
        let ca_output_w: Vec<u16> = ca_output_text.encode_utf16().collect();
        SetWindowTextW(HOME_CA_OUTPUT_HWND, ca_output_w.as_ptr());
    }

    if !HOME_CA_POWER_HWND.is_null() {
        let ca_power_text = if ca_connected {
            if ca_power { "CA Power: On\0" } else { "CA Power: Standby\0" }
        } else {
            "CA Power: —\0"
        };
        let ca_power_w: Vec<u16> = ca_power_text.encode_utf16().collect();
        SetWindowTextW(HOME_CA_POWER_HWND, ca_power_w.as_ptr());
    }

    // Update Connection tab
    if !STATUS_LABEL_HWND.is_null() {
        let status = if ca_connected {
            format!("Status: Connected - {}\0", ca_device)
        } else {
            "Status: Not connected\0".to_string()
        };
        let status_w: Vec<u16> = status.encode_utf16().collect();
        SetWindowTextW(STATUS_LABEL_HWND, status_w.as_ptr());
    }

    if !WIN_VOL_LABEL_HWND.is_null() {
        let vol_text = if muted {
            format!("Windows Volume: {:.0}% (Muted)\0", vol_pct)
        } else {
            format!("Windows Volume: {:.0}%\0", vol_pct)
        };
        let vol_w: Vec<u16> = vol_text.encode_utf16().collect();
        SetWindowTextW(WIN_VOL_LABEL_HWND, vol_w.as_ptr());
    }

    // Update Logs tab
    if !LOG_LIST_HWND.is_null() {
        let count = SendMessageW(LOG_LIST_HWND, LB_GETCOUNT, 0, 0) as usize;
        if count < log_entries.len() {
            // Collect new entries into wide strings while holding no lock
            let new_entries: Vec<Vec<u16>> = log_entries.iter()
                .skip(count)
                .map(|e| format!("{}\0", e).encode_utf16().collect())
                .collect();
            for text in &new_entries {
                SendMessageW(LOG_LIST_HWND, LB_ADDSTRING, 0, text.as_ptr() as LPARAM);
            }
            let new_count = SendMessageW(LOG_LIST_HWND, LB_GETCOUNT, 0, 0);
            if new_count > 0 {
                SendMessageW(
                    LOG_LIST_HWND,
                    LB_SETTOPINDEX,
                    (new_count - 1) as usize,
                    0,
                );
            }
        }
    }
}

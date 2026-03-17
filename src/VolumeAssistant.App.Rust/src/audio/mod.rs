#![allow(non_snake_case, non_camel_case_types, dead_code, non_upper_case_globals)]

use windows_sys::core::GUID;
use windows_sys::Win32::Foundation::{S_OK, BOOL};
use windows_sys::Win32::System::Com::{CoCreateInstance, CLSCTX_ALL};
use windows_sys::Win32::Media::Audio::{eRender, eConsole};

const CLSID_MMDeviceEnumerator: GUID = GUID {
    data1: 0xBCDE0395,
    data2: 0xE52F,
    data3: 0x467C,
    data4: [0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E],
};
const IID_IMMDeviceEnumerator: GUID = GUID {
    data1: 0xA95664D2,
    data2: 0x9614,
    data3: 0x4F35,
    data4: [0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6],
};
const IID_IAudioEndpointVolume: GUID = GUID {
    data1: 0x5CDF2C82,
    data2: 0x841E,
    data3: 0x4546,
    data4: [0x97, 0x22, 0x0C, 0xF7, 0x40, 0x78, 0x22, 0x9A],
};

type QueryInterfaceFn = unsafe extern "system" fn(
    *mut std::ffi::c_void,
    *const GUID,
    *mut *mut std::ffi::c_void,
) -> i32;
type AddRefFn = unsafe extern "system" fn(*mut std::ffi::c_void) -> u32;
type ReleaseFn = unsafe extern "system" fn(*mut std::ffi::c_void) -> u32;

#[repr(C)]
struct IMMDeviceEnumeratorVtbl {
    QueryInterface: QueryInterfaceFn,
    AddRef: AddRefFn,
    Release: ReleaseFn,
    EnumAudioEndpoints: unsafe extern "system" fn(
        *mut std::ffi::c_void,
        u32,
        u32,
        *mut *mut std::ffi::c_void,
    ) -> i32,
    GetDefaultAudioEndpoint: unsafe extern "system" fn(
        *mut std::ffi::c_void,
        u32,
        u32,
        *mut *mut std::ffi::c_void,
    ) -> i32,
}

#[repr(C)]
struct IMMDeviceEnumerator {
    vtbl: *const IMMDeviceEnumeratorVtbl,
}

#[repr(C)]
struct IMMDeviceVtbl {
    QueryInterface: QueryInterfaceFn,
    AddRef: AddRefFn,
    Release: ReleaseFn,
    Activate: unsafe extern "system" fn(
        *mut std::ffi::c_void,
        *const GUID,
        u32,
        *mut std::ffi::c_void,
        *mut *mut std::ffi::c_void,
    ) -> i32,
}

#[repr(C)]
struct IMMDevice {
    vtbl: *const IMMDeviceVtbl,
}

#[repr(C)]
struct IAudioEndpointVolumeVtbl {
    QueryInterface: QueryInterfaceFn,
    AddRef: AddRefFn,
    Release: ReleaseFn,
    RegisterControlChangeNotify:
        unsafe extern "system" fn(*mut std::ffi::c_void, *mut std::ffi::c_void) -> i32,
    UnregisterControlChangeNotify:
        unsafe extern "system" fn(*mut std::ffi::c_void, *mut std::ffi::c_void) -> i32,
    GetChannelCount: unsafe extern "system" fn(*mut std::ffi::c_void, *mut u32) -> i32,
    SetMasterVolumeLevel:
        unsafe extern "system" fn(*mut std::ffi::c_void, f32, *const GUID) -> i32,
    SetMasterVolumeLevelScalar:
        unsafe extern "system" fn(*mut std::ffi::c_void, f32, *const GUID) -> i32,
    GetMasterVolumeLevel: unsafe extern "system" fn(*mut std::ffi::c_void, *mut f32) -> i32,
    GetMasterVolumeLevelScalar: unsafe extern "system" fn(*mut std::ffi::c_void, *mut f32) -> i32,
    SetChannelVolumeLevel:
        unsafe extern "system" fn(*mut std::ffi::c_void, u32, f32, *const GUID) -> i32,
    SetChannelVolumeLevelScalar:
        unsafe extern "system" fn(*mut std::ffi::c_void, u32, f32, *const GUID) -> i32,
    GetChannelVolumeLevel:
        unsafe extern "system" fn(*mut std::ffi::c_void, u32, *mut f32) -> i32,
    GetChannelVolumeLevelScalar:
        unsafe extern "system" fn(*mut std::ffi::c_void, u32, *mut f32) -> i32,
    SetMute: unsafe extern "system" fn(*mut std::ffi::c_void, BOOL, *const GUID) -> i32,
    GetMute: unsafe extern "system" fn(*mut std::ffi::c_void, *mut BOOL) -> i32,
    GetVolumeStepInfo:
        unsafe extern "system" fn(*mut std::ffi::c_void, *mut u32, *mut u32) -> i32,
    VolumeStepUp: unsafe extern "system" fn(*mut std::ffi::c_void, *const GUID) -> i32,
    VolumeStepDown: unsafe extern "system" fn(*mut std::ffi::c_void, *const GUID) -> i32,
    QueryHardwareSupport: unsafe extern "system" fn(*mut std::ffi::c_void, *mut u32) -> i32,
    GetVolumeRange:
        unsafe extern "system" fn(*mut std::ffi::c_void, *mut f32, *mut f32, *mut f32) -> i32,
}

#[repr(C)]
struct IAudioEndpointVolume {
    vtbl: *const IAudioEndpointVolumeVtbl,
}

pub struct AudioController {
    endpoint_volume: Option<*mut IAudioEndpointVolume>,
}

// SAFETY: IAudioEndpointVolume is a thread-safe COM interface (its reference-counted
// vtable methods serialize concurrent callers internally).  The raw pointer stored here
// is valid for the entire process lifetime (allocated once in `new` and released in
// `Drop`).  The surrounding `Mutex<AudioController>` ensures at most one thread calls
// into the COM interface at a time, satisfying COM apartment threading requirements for
// MTA initialization (CoInitializeEx with COINIT_MULTITHREADED in main).
unsafe impl Send for AudioController {}
unsafe impl Sync for AudioController {}

impl AudioController {
    pub fn new() -> Self {
        let endpoint_volume = unsafe { Self::init_wasapi() };
        AudioController { endpoint_volume }
    }

    unsafe fn init_wasapi() -> Option<*mut IAudioEndpointVolume> {
        let mut enumerator: *mut IMMDeviceEnumerator = std::ptr::null_mut();
        let hr = CoCreateInstance(
            &CLSID_MMDeviceEnumerator,
            std::ptr::null_mut(),
            CLSCTX_ALL,
            &IID_IMMDeviceEnumerator,
            &mut enumerator as *mut _ as *mut *mut std::ffi::c_void,
        );
        if hr != S_OK || enumerator.is_null() {
            return None;
        }

        let mut device: *mut IMMDevice = std::ptr::null_mut();
        let hr = ((*(*enumerator).vtbl).GetDefaultAudioEndpoint)(
            enumerator as *mut _,
            eRender as u32,
            eConsole as u32,
            &mut device as *mut _ as *mut *mut std::ffi::c_void,
        );
        ((*(*enumerator).vtbl).Release)(enumerator as *mut _);

        if hr != S_OK || device.is_null() {
            return None;
        }

        let mut endpoint_vol: *mut IAudioEndpointVolume = std::ptr::null_mut();
        let hr = ((*(*device).vtbl).Activate)(
            device as *mut _,
            &IID_IAudioEndpointVolume,
            CLSCTX_ALL,
            std::ptr::null_mut(),
            &mut endpoint_vol as *mut _ as *mut *mut std::ffi::c_void,
        );
        ((*(*device).vtbl).Release)(device as *mut _);

        if hr != S_OK || endpoint_vol.is_null() {
            return None;
        }

        Some(endpoint_vol)
    }

    pub fn get_volume_scalar(&self) -> f32 {
        if let Some(ep) = self.endpoint_volume {
            let mut vol = 0.0f32;
            unsafe {
                ((*(*ep).vtbl).GetMasterVolumeLevelScalar)(ep as *mut _, &mut vol);
            }
            vol
        } else {
            0.5
        }
    }

    pub fn set_volume_scalar(&self, scalar: f32) {
        if let Some(ep) = self.endpoint_volume {
            let clamped = scalar.clamp(0.0, 1.0);
            unsafe {
                ((*(*ep).vtbl).SetMasterVolumeLevelScalar)(
                    ep as *mut _,
                    clamped,
                    std::ptr::null(),
                );
            }
        }
    }

    pub fn get_volume_percent(&self) -> f32 {
        self.get_volume_scalar() * 100.0
    }

    pub fn set_volume_percent(&self, percent: f32) {
        self.set_volume_scalar(percent / 100.0);
    }

    pub fn get_muted(&self) -> bool {
        if let Some(ep) = self.endpoint_volume {
            let mut muted: BOOL = 0;
            unsafe {
                ((*(*ep).vtbl).GetMute)(ep as *mut _, &mut muted);
            }
            muted != 0
        } else {
            false
        }
    }

    pub fn set_muted(&self, muted: bool) {
        if let Some(ep) = self.endpoint_volume {
            unsafe {
                ((*(*ep).vtbl).SetMute)(
                    ep as *mut _,
                    if muted { 1 } else { 0 },
                    std::ptr::null(),
                );
            }
        }
    }
}

impl Drop for AudioController {
    fn drop(&mut self) {
        if let Some(ep) = self.endpoint_volume {
            unsafe {
                ((*(*ep).vtbl).Release)(ep as *mut _);
            }
        }
    }
}

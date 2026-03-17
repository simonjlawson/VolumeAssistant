#![allow(non_snake_case)]

use windows_sys::Win32::Foundation::{RECT, BOOL};
use windows_sys::Win32::Graphics::Gdi::{
    CreateCompatibleDC, CreateBitmap, SelectObject, DeleteObject, DeleteDC,
    SetPixel, CreateCompatibleBitmap, GetDC, ReleaseDC,
    CreateSolidBrush, FillRect,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    HICON, ICONINFO, CreateIconIndirect,
};

#[inline]
fn rgb(r: u8, g: u8, b: u8) -> u32 {
    (r as u32) | ((g as u32) << 8) | ((b as u32) << 16)
}

pub fn create_volume_icon(volume_percent: f32, muted: bool) -> HICON {
    const SIZE: i32 = 16;

    unsafe {
        let mask_bits: Vec<u8> = vec![0u8; (SIZE * SIZE / 8) as usize];

        let dc = GetDC(std::ptr::null_mut());
        let mem_dc = CreateCompatibleDC(dc);
        ReleaseDC(std::ptr::null_mut(), dc);

        let hbm_color = CreateCompatibleBitmap(mem_dc, SIZE, SIZE);
        let old_bm = SelectObject(mem_dc, hbm_color);

        let black_brush = CreateSolidBrush(rgb(0, 0, 0));
        let rect = RECT { left: 0, top: 0, right: SIZE, bottom: SIZE };
        FillRect(mem_dc, &rect, black_brush);
        DeleteObject(black_brush);

        let cx = SIZE as f32 / 2.0;
        let cy = SIZE as f32 / 2.0;
        let r = SIZE as f32 * 0.4;

        let color = if muted { rgb(180, 180, 180) } else { rgb(255, 255, 255) };

        let steps = 100;
        for i in 0..steps {
            let angle_deg = 135.0 + (270.0 * i as f32 / steps as f32);
            let angle_rad = angle_deg * std::f32::consts::PI / 180.0;
            let px = (cx + r * angle_rad.cos()).round() as i32;
            let py = (cy + r * angle_rad.sin()).round() as i32;
            if px >= 0 && px < SIZE && py >= 0 && py < SIZE {
                SetPixel(mem_dc, px, py, color);
            }
        }

        let p = volume_percent.clamp(0.0, 100.0);
        let angle_rad = -std::f32::consts::PI / 2.0
            + ((p - 50.0) / 100.0) * (3.0 * std::f32::consts::PI / 2.0);
        let ind_len = r * 0.52;
        let end_x = (cx + ind_len * angle_rad.cos()).round() as i32;
        let end_y = (cy + ind_len * angle_rad.sin()).round() as i32;

        let dx = end_x - cx as i32;
        let dy = end_y - cy as i32;
        let steps_line = (dx.abs().max(dy.abs())) as usize + 1;
        for j in 0..=steps_line {
            let t = if steps_line > 0 { j as f32 / steps_line as f32 } else { 0.0 };
            let lx = (cx + t * dx as f32).round() as i32;
            let ly = (cy + t * dy as f32).round() as i32;
            if lx >= 0 && lx < SIZE && ly >= 0 && ly < SIZE {
                SetPixel(mem_dc, lx, ly, color);
            }
        }

        SelectObject(mem_dc, old_bm);
        DeleteDC(mem_dc);

        let hbm_mask = CreateBitmap(
            SIZE,
            SIZE,
            1,
            1,
            mask_bits.as_ptr() as *const std::ffi::c_void,
        );

        let icon_info = ICONINFO {
            fIcon: 1,
            xHotspot: 0,
            yHotspot: 0,
            hbmMask: hbm_mask,
            hbmColor: hbm_color,
        };

        let icon = CreateIconIndirect(&icon_info);

        DeleteObject(hbm_mask);
        DeleteObject(hbm_color);

        icon
    }
}

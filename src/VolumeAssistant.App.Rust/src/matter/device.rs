#[derive(Debug, Default, Clone)]
pub struct MatterDevice {
    pub volume_percent: f32,
    pub muted: bool,
    pub on_off: bool,
    pub vendor_id: u16,
    pub product_id: u16,
    pub discriminator: u16,
}

impl MatterDevice {
    pub fn new(vendor_id: u16, product_id: u16, discriminator: u16) -> Self {
        MatterDevice {
            volume_percent: 50.0,
            muted: false,
            on_off: true,
            vendor_id,
            product_id,
            discriminator,
        }
    }

    pub fn update_from_volume(&mut self, percent: f32, muted: bool) {
        self.volume_percent = percent.clamp(0.0, 100.0);
        self.muted = muted;
        self.on_off = !muted;
    }

    pub fn level_control_current_level(&self) -> u8 {
        (self.volume_percent / 100.0 * 254.0) as u8
    }
}

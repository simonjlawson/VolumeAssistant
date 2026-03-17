#[derive(Debug, Clone, PartialEq)]
pub enum TlvType {
    SignedInt(i64),
    UnsignedInt(u64),
    Boolean(bool),
    Float(f32),
    Double(f64),
    Str(Vec<u8>),
    ByteStr(Vec<u8>),
    Null,
    Structure(Vec<TlvElement>),
    Array(Vec<TlvElement>),
    EndOfContainer,
}

#[derive(Debug, Clone, PartialEq)]
pub struct TlvElement {
    pub tag: TlvTag,
    pub value: TlvType,
}

#[derive(Debug, Clone, PartialEq)]
pub enum TlvTag {
    Anonymous,
    Context(u8),
    CommonProfile2(u16),
    CommonProfile4(u32),
    FullyQualified6(u32, u32),
    FullyQualified8(u32, u32),
}

pub fn encode_tlv(elements: &[TlvElement]) -> Vec<u8> {
    let mut buf = Vec::new();
    for elem in elements {
        encode_element(&mut buf, elem);
    }
    buf
}

fn tag_control(tag: &TlvTag) -> u8 {
    match tag {
        TlvTag::Anonymous => 0x00,
        TlvTag::Context(_) => 0x20,
        TlvTag::CommonProfile2(_) => 0x40,
        TlvTag::CommonProfile4(_) => 0x60,
        TlvTag::FullyQualified6(_, _) => 0x80,
        TlvTag::FullyQualified8(_, _) => 0xA0,
    }
}

fn encode_element(buf: &mut Vec<u8>, elem: &TlvElement) {
    let tc = tag_control(&elem.tag);
    match &elem.value {
        TlvType::UnsignedInt(v) => {
            if *v <= 0xFF {
                buf.push(tc | 0x04);
                push_tag(buf, &elem.tag);
                buf.push(*v as u8);
            } else if *v <= 0xFFFF {
                buf.push(tc | 0x05);
                push_tag(buf, &elem.tag);
                buf.extend_from_slice(&(*v as u16).to_le_bytes());
            } else {
                buf.push(tc | 0x06);
                push_tag(buf, &elem.tag);
                buf.extend_from_slice(&(*v as u32).to_le_bytes());
            }
        }
        TlvType::SignedInt(v) => {
            if *v >= i8::MIN as i64 && *v <= i8::MAX as i64 {
                buf.push(tc | 0x00);
                push_tag(buf, &elem.tag);
                buf.push(*v as i8 as u8);
            } else {
                buf.push(tc | 0x02);
                push_tag(buf, &elem.tag);
                buf.extend_from_slice(&(*v as i32).to_le_bytes());
            }
        }
        TlvType::Boolean(b) => {
            buf.push(tc | if *b { 0x09 } else { 0x08 });
            push_tag(buf, &elem.tag);
        }
        TlvType::Null => {
            buf.push(tc | 0x14);
            push_tag(buf, &elem.tag);
        }
        TlvType::Structure(children) => {
            buf.push(tc | 0x15);
            push_tag(buf, &elem.tag);
            for child in children {
                encode_element(buf, child);
            }
            buf.push(0x18);
        }
        TlvType::Array(children) => {
            buf.push(tc | 0x16);
            push_tag(buf, &elem.tag);
            for child in children {
                encode_element(buf, child);
            }
            buf.push(0x18);
        }
        TlvType::Str(s) => {
            buf.push(tc | 0x0C);
            push_tag(buf, &elem.tag);
            buf.push(s.len() as u8);
            buf.extend_from_slice(s);
        }
        TlvType::ByteStr(s) => {
            buf.push(tc | 0x10);
            push_tag(buf, &elem.tag);
            buf.push(s.len() as u8);
            buf.extend_from_slice(s);
        }
        _ => {}
    }
}

fn push_tag(buf: &mut Vec<u8>, tag: &TlvTag) {
    match tag {
        TlvTag::Anonymous => {}
        TlvTag::Context(v) => buf.push(*v),
        TlvTag::CommonProfile2(v) => buf.extend_from_slice(&v.to_le_bytes()),
        TlvTag::CommonProfile4(v) => buf.extend_from_slice(&v.to_le_bytes()),
        _ => {}
    }
}

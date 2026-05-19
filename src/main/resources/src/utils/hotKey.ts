const MOD_CTRL = 0x0002;
const MOD_ALT = 0x0001;

const ALLOWED_MASK = MOD_CTRL | MOD_ALT;

// shift ctrl alt lwin rwin
const MODIFIER_VKS = new Set([16, 17, 18, 91, 92]); 

const NUMPAD_MAP: Record<number, string> = {
  0x6a: "NUMPAD *",
  0x6b: "NUMPAD +",
  0x6d: "NUMPAD -",
  0x6e: "NUMPAD .",
  0x6f: "NUMPAD /",
};

const VK_MAP: Record<number, string> = {
  0x1b: "ESC",
  0x20: "SPACE",
  0x0d: "ENTER",
  0x09: "TAB",
  0x08: "BACKSPACE",
  0x2e: "DELETE",
  0x2d: "INSERT",
  0x24: "HOME",
  0x23: "END",
  0x21: "PAGE UP",
  0x22: "PAGE DOWN",
  0x25: "LEFT",
  0x26: "UP",
  0x27: "RIGHT",
  0x28: "DOWN",
  0xba: ";",
  0xbb: "=",
  0xbc: ",",
  0xbd: "-",
  0xbe: ".",
  0xbf: "/",
  0xc0: "`",
  0xdb: "[",
  0xdc: "\\",
  0xdd: "]",
  0xde: "'",
};

export const vkToKeyLabel = (vk: number) => {
  if (!Number.isFinite(vk)) return "";
  if (vk >= 0x30 && vk <= 0x39) return String.fromCharCode(vk); // 0~9
  if (vk >= 0x41 && vk <= 0x5a) return String.fromCharCode(vk); // A~Z
  if (vk >= 0x70 && vk <= 0x87) return `F${vk - 0x6f}`;         // F1~F24
  if (vk >= 0x60 && vk <= 0x69) return `NUMPAD ${vk - 0x60}`;   // Numpad 0~9
  return NUMPAD_MAP[vk] || VK_MAP[vk] || `VK_${vk}`;
};

export const formatHotkey = (modifiers: number, vkCode: number) => {
  const parts: string[] = [];
  if (modifiers & MOD_CTRL) parts.push("CTRL");
  if (modifiers & MOD_ALT) parts.push("ALT");
  const key = vkToKeyLabel(vkCode);
  if (key) parts.push(key);
  return parts.join(" + ");
};

export const parseHotkeyString = (raw: unknown) => {
  const s = String(raw || "");
  const m = s.match(/modifiers\s*=\s*(\d+)[\s\S]*?vkCode\s*=\s*(\d+)/i);
  if (!m) return null;

  const modifiers = Number(m[1]) & ALLOWED_MASK;
  const vkCode = Number(m[2]);

  if (!modifiers) return null;
  if (!Number.isFinite(vkCode) || MODIFIER_VKS.has(vkCode)) return null;

  return { modifiers, vkCode };
};


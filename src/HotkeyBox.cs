using System;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// 快捷键录制输入框：只读，获得焦点后直接按键即可录入组合键。
// 规则：必须带 Ctrl/Alt/Shift 之一，F1–F12 可单独使用；录制成功抛 HotkeyRecorded。
// ---------------------------------------------------------------------------

namespace Launchpad
{
    /// <summary>点击后按下组合键即可录制快捷键的输入框。</summary>
    internal sealed class HotkeyBox : TextBox
    {
        public event EventHandler HotkeyRecorded;

        public uint Modifiers { get; private set; }
        public uint Key { get; private set; }

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
        }

        public void SetHotkey(uint modifiers, uint key)
        {
            Modifiers = modifiers;
            Key = key;
            Text = AppSettings.FormatHotkey(modifiers, key);
        }

        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (!Focused) return base.ProcessCmdKey(ref m, keyData);

            Keys code = keyData & Keys.KeyCode;

            if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey
                || code == Keys.LControlKey || code == Keys.RControlKey
                || code == Keys.LMenu || code == Keys.RMenu
                || code == Keys.LShiftKey || code == Keys.RShiftKey)
            {
                return true;
            }

            uint modifiers = 0;
            if ((keyData & Keys.Control) != 0) modifiers |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) modifiers |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) modifiers |= Native.MOD_SHIFT;

            // 单独一个普通按键太容易和别的软件冲突，要求至少带一个修饰键；F1-F12 例外
            bool functionKey = code >= Keys.F1 && code <= Keys.F12;
            if (modifiers == 0 && !functionKey) return true;

            Modifiers = modifiers;
            Key = (uint)code;
            Text = AppSettings.FormatHotkey(Modifiers, Key);

            if (HotkeyRecorded != null) HotkeyRecorded(this, EventArgs.Empty);
            return true;
        }
    }
}

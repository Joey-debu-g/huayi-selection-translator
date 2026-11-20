namespace HuaYi
{
    internal enum UseMode { Paused, Manual, Automatic }
    internal enum Trigger { Drag, DoubleClick, Shortcut, Clipboard }

    internal static class TriggerPolicy
    {
        internal static bool Allows(UseMode mode, Trigger trigger)
        {
            if (mode == UseMode.Paused) return false;
            if (trigger == Trigger.Shortcut || trigger == Trigger.Clipboard) return true;
            return mode == UseMode.Automatic && (trigger == Trigger.Drag || trigger == Trigger.DoubleClick);
        }
    }
}

using System;

namespace YAFC.UI {
    public static class MathUtils {
        public static float Clamp(float value, float min, float max) {
            return value < min ? min : value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max) {
            return value < min ? min : value > max ? max : value;
        }

        public static int Round(float value) {
            return (int)MathF.Round(value);
        }

        public static int Floor(float value) {
            return (int)MathF.Floor(value);
        }

        public static int Ceil(float value) {
            return (int)MathF.Ceiling(value);
        }

        public static byte FloatToByte(float f) {
            return f <= 0 ? (byte)0 : f >= 1 ? (byte)255 : (byte)MathF.Round(f * 255);
        }

        public static float LogarithmicToLinear(float value, float logmin, float logmax) {
            if (value < 0f) {
                value = 0f;
            }

            float cur = MathF.Log(value);
            return cur <= logmin ? 0f : cur >= logmax ? 1f : (cur - logmin) / (logmax - logmin);
        }

        public static float LinearToLogarithmic(float value, float logmin, float logmax, float min, float max) {
            if (value <= 0f) {
                return min;
            }

            if (value >= 1f) {
                return max;
            }

            float logcur = logmin + ((logmax - logmin) * value);
            return MathF.Exp(logcur);
        }
    }
}

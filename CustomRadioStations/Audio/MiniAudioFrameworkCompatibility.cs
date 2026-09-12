using MiniAudioEx.Core.StandardAPI;
using MiniAudioEx.Native;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomRadioStations {
    /// <summary>
    /// Works around MiniAudioEx 3.3.6 returning a native structure that contains a
    /// managed delegate. The .NET Framework CLR rejects that P/Invoke signature,
    /// while the equivalent blittable function-pointer layout works correctly.
    /// </summary>
    internal static class MiniAudioFrameworkCompatibility {
        private const string NativeLibraryName = "miniaudioex";
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfo {
            internal IntPtr Name;
            internal int Index;
            internal int IsDefault;
            internal uint NativeDataFormatCount;
            internal IntPtr NativeDataFormats;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ContextConfig {
            internal DeviceInfo DeviceInfo;
            internal uint SampleRate;
            internal byte Channels;
            internal uint PeriodSizeInFrames;
            internal IntPtr DeviceDataProc;
        }

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ContextConfig ma_ex_context_config_init(
            uint sampleRate, byte channels, uint periodSizeInFrames, ref DeviceInfo deviceInfo);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ma_ex_context_init(ref ContextConfig config);

        internal static void Initialize(uint sampleRate, uint channels, uint periodSizeInFrames = 0) {
            if (channels == 0 || channels > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(channels));

            Type contextType = typeof(AudioContext);
            FieldInfo contextField = RequireField(contextType, "audioContext");
            if ((IntPtr)contextField.GetValue(null) != IntPtr.Zero)
                throw new InvalidOperationException("MiniAudioEx is already initialized.");

            MethodInfo callbackMethod = contextType.GetMethod("OnDeviceDataProc", PrivateStatic);
            if (callbackMethod == null)
                throw new MissingMethodException(contextType.FullName, "OnDeviceDataProc");

            var callback = (ma_device_data_proc)Delegate.CreateDelegate(typeof(ma_device_data_proc), callbackMethod);
            var deviceInfo = new DeviceInfo { Index = -1 };
            ContextConfig config = ma_ex_context_config_init(
                sampleRate, (byte)channels, periodSizeInFrames, ref deviceInfo);
            config.DeviceDataProc = Marshal.GetFunctionPointerForDelegate(callback);

            IntPtr nativeContext = ma_ex_context_init(ref config);
            if (nativeContext == IntPtr.Zero)
                throw new InvalidOperationException("Failed to initialize MiniAudioEx.");

            // Preserve the delegate for the lifetime of the native context and set the
            // same private state that AudioContext.Initialize normally establishes.
            RequireField(contextType, "deviceDataProc").SetValue(null, callback);
            RequireField(contextType, "sampleRate").SetValue(null, sampleRate);
            RequireField(contextType, "channels").SetValue(null, channels);
            contextField.SetValue(null, nativeContext);
            RequireField(contextType, "lastUpdateTime").SetValue(null, DateTime.Now);
        }

        private static FieldInfo RequireField(Type type, string name) {
            FieldInfo field = type.GetField(name, PrivateStatic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}

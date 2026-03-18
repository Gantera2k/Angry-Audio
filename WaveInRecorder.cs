// WaveInRecorder.cs — Safe winmm audio recorder using CALLBACK_EVENT.
//
// Supports two modes:
//   1. One-shot: Start() → StopAndGetWav() (legacy)
//   2. Warm (pre-buffered): WarmUp() keeps mic open, MarkCaptureStart()
//      marks the moment the user presses the key, StopAndGetWav() returns
//      audio from mark point (minus a small pre-buffer) to now.
//
// The warm mode eliminates the 200-500ms device-open delay, making
// dictation feel instant. A 500ms ring pre-buffer captures the first
// syllables that would otherwise be missed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AngryAudio
{
    public class WaveInRecorder : IDisposable
    {
        // ── Win32 ────────────────────────────────────────────────────────────
        [DllImport("winmm.dll")] static extern int  waveInOpen            (out IntPtr h, uint dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int  waveInPrepareHeader   (IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int  waveInAddBuffer       (IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int  waveInStart           (IntPtr h);
        [DllImport("winmm.dll")] static extern int  waveInStop            (IntPtr h);
        [DllImport("winmm.dll")] static extern int  waveInReset           (IntPtr h);
        [DllImport("winmm.dll")] static extern int  waveInUnprepareHeader (IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int  waveInClose           (IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool init, string name);
        [DllImport("kernel32.dll")] static extern bool   SetEvent          (IntPtr h);
        [DllImport("kernel32.dll")] static extern bool   CloseHandle       (IntPtr h);
        [DllImport("kernel32.dll")] static extern uint   WaitForSingleObject(IntPtr h, uint ms);

        const uint WAVE_MAPPER    = unchecked((uint)-1);
        const uint CALLBACK_EVENT = 0x00050000;
        const uint WHDR_DONE      = 0x00000001;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct WAVEFORMATEX {
            public ushort wFormatTag, nChannels;
            public uint   nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR {
            public IntPtr lpData;
            public uint   dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint   dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        // ── Audio format ─────────────────────────────────────────────────────
        const int SAMPLE_RATE = 16000;
        const int CHANNELS    = 1;
        const int BITS        = 16;
        const int BUFFER_MS   = 100;
        const int NUM_BUFS    = 4;
        const int BUFFER_SIZE = SAMPLE_RATE * CHANNELS * (BITS / 8) * BUFFER_MS / 1000;
        const int BYTES_PER_SEC = SAMPLE_RATE * CHANNELS * (BITS / 8);

        // Pre-buffer: capture 500ms BEFORE the key press so first syllables aren't lost
        const int PRE_BUFFER_MS    = 500;
        const int PRE_BUFFER_BYTES = BYTES_PER_SEC * PRE_BUFFER_MS / 1000;

        // ── State ────────────────────────────────────────────────────────────
        IntPtr     _hWaveIn;
        IntPtr     _hEvent;
        IntPtr[]   _hdrPtrs = new IntPtr[NUM_BUFS];
        GCHandle[] _bufPins = new GCHandle[NUM_BUFS];
        byte[][]   _bufs    = new byte[NUM_BUFS][];

        // Ring buffer for warm mode — holds recent audio continuously
        byte[]   _ring = new byte[PRE_BUFFER_BYTES * 4]; // 2 seconds of ring buffer
        int      _ringHead;      // write position
        int      _ringTotal;     // total bytes written (for knowing how much is valid)
        readonly object _ringLock = new object();

        // Capture buffer — audio from MarkCaptureStart() onwards
        List<byte> _pcm = new List<byte>(SAMPLE_RATE * 30);
        volatile bool _capturing;   // true between MarkCaptureStart and StopAndGetWav
        int _captureMarkRingTotal;   // ring total at the moment of MarkCaptureStart

        volatile bool _running;
        Thread     _thread;
        readonly object _lock = new object();
        bool _isWarm; // true when in warm/continuous mode

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Start in one-shot mode (legacy). Creates device, records, then StopAndGetWav() tears it down.</summary>
        public void Start()
        {
            _isWarm = false;
            OpenDevice();
        }

        /// <summary>Start in warm mode — mic stays open continuously for instant response.
        /// Call MarkCaptureStart() when hotkey pressed, StopAndGetWav() when released.</summary>
        public void WarmUp()
        {
            if (_running) return;
            _isWarm = true;
            _capturing = false;
            lock (_ringLock) { _ringHead = 0; _ringTotal = 0; }
            OpenDevice();
            Logger.Info("WaveInRecorder: warmed up — mic standing by.");
        }

        /// <summary>Mark the start of a capture. Audio from this point will be returned by StopCapture().
        /// Ring buffer is cleared to prevent stale audio from previous session leaking into pre-buffer.</summary>
        public void MarkCaptureStart()
        {
            lock (_lock) { _pcm = new List<byte>(SAMPLE_RATE * 30); }
            lock (_ringLock) {
                // Clear ring to prevent previous session's audio from contaminating the pre-buffer.
                // The warm recorder starts capturing instantly (no device-open delay), so we don't
                // need the pre-buffer to catch first syllables — they'll be in _pcm directly.
                _ringHead = 0;
                _ringTotal = 0;
                Array.Clear(_ring, 0, _ring.Length);
                _captureMarkRingTotal = 0;
            }
            _capturing = true;
            Logger.Info("WaveInRecorder: capture marked (ring cleared).");
        }

        /// <summary>Stop capturing and return WAV of the captured segment (with pre-buffer). Mic stays open for next press.</summary>
        public byte[] StopCapture()
        {
            _capturing = false;

            byte[] preBuffer;
            byte[] capturedPcm;

            lock (_ringLock)
            {
                // Grab pre-buffer from ring: the PRE_BUFFER_BYTES before MarkCaptureStart
                int available = Math.Min(_captureMarkRingTotal, PRE_BUFFER_BYTES);
                available = Math.Min(available, _ring.Length);
                preBuffer = new byte[available];
                if (available > 0)
                {
                    // Calculate where the pre-buffer starts in the ring
                    int markHead = _captureMarkRingTotal % _ring.Length;
                    int preStart = (markHead - available + _ring.Length) % _ring.Length;
                    for (int i = 0; i < available; i++)
                        preBuffer[i] = _ring[(preStart + i) % _ring.Length];
                }
                
                // CRITICAL: Reset the ring buffer so rapid back-to-back dictations don't 
                // accidentally capture the trailing tail of the previous sentence (double-dictation).
                _ringTotal = 0;
                _ringHead = 0;
                Array.Clear(_ring, 0, _ring.Length);
            }

            lock (_lock) { capturedPcm = _pcm.ToArray(); _pcm.Clear(); }

            // Combine pre-buffer + captured audio
            byte[] fullPcm = new byte[preBuffer.Length + capturedPcm.Length];
            Buffer.BlockCopy(preBuffer, 0, fullPcm, 0, preBuffer.Length);
            Buffer.BlockCopy(capturedPcm, 0, fullPcm, preBuffer.Length, capturedPcm.Length);

            Logger.Info("WaveInRecorder: capture stopped. Pre=" + preBuffer.Length + " + Main=" + capturedPcm.Length +
                " = " + fullPcm.Length + " bytes (" + (fullPcm.Length / (double)BYTES_PER_SEC).ToString("F2") + "s).");

            return BuildWav(fullPcm);
        }

        /// <summary>Shut down the warm recorder entirely (called when dictation mode is disabled).</summary>
        public void Shutdown()
        {
            if (!_running) return;
            _capturing = false;
            _running = false;

            if (_hWaveIn != IntPtr.Zero) {
                waveInStop (_hWaveIn);
                waveInReset(_hWaveIn);
            }
            if (_hEvent != IntPtr.Zero) SetEvent(_hEvent);
            if (_thread != null) _thread.Join(1000);

            CleanupDevice();
            Logger.Info("WaveInRecorder: shut down.");
        }

        /// <summary>Legacy: stop recording and return full WAV (one-shot mode).</summary>
        public byte[] StopAndGetWav()
        {
            if (_isWarm && _capturing)
                return StopCapture();

            _running = false;

            if (_hWaveIn != IntPtr.Zero) {
                waveInStop (_hWaveIn);
                waveInReset(_hWaveIn);
            }
            if (_hEvent != IntPtr.Zero) SetEvent(_hEvent);
            if (_thread != null) _thread.Join(1000);

            byte[] pcm;
            lock (_lock) { pcm = _pcm.ToArray(); }
            Logger.Info("WaveInRecorder: stopped. Captured " + pcm.Length + " bytes (" +
                (pcm.Length / (double)BYTES_PER_SEC).ToString("F2") + "s).");

            CleanupDevice();

            return BuildWav(pcm);
        }

        public void Dispose()
        {
            try { Shutdown(); } catch { }
        }

        /// <summary>True if the device is open and running.</summary>
        public bool IsRunning { get { return _running; } }

        public float CurrentPeakLevel { get; private set; }

        // ── Internal ─────────────────────────────────────────────────────────

        void OpenDevice()
        {
            _pcm     = new List<byte>(SAMPLE_RATE * 30);
            _running = true;

            _hEvent = CreateEvent(IntPtr.Zero, false, false, null);
            if (_hEvent == IntPtr.Zero)
                throw new InvalidOperationException("WaveInRecorder: CreateEvent failed");

            var fmt = new WAVEFORMATEX {
                wFormatTag      = 1,
                nChannels       = CHANNELS,
                nSamplesPerSec  = SAMPLE_RATE,
                nAvgBytesPerSec = (uint)BYTES_PER_SEC,
                nBlockAlign     = (ushort)(CHANNELS * BITS / 8),
                wBitsPerSample  = BITS
            };

            int r = waveInOpen(out _hWaveIn, WAVE_MAPPER, ref fmt, _hEvent, IntPtr.Zero, CALLBACK_EVENT);
            if (r != 0) {
                CloseHandle(_hEvent); _hEvent = IntPtr.Zero;
                throw new InvalidOperationException("WaveInRecorder: waveInOpen failed: " + r);
            }

            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < NUM_BUFS; i++) {
                _bufs[i]    = new byte[BUFFER_SIZE];
                _bufPins[i] = GCHandle.Alloc(_bufs[i], GCHandleType.Pinned);

                _hdrPtrs[i] = Marshal.AllocHGlobal(hdrSz);
                for (int b = 0; b < hdrSz; b++) Marshal.WriteByte(_hdrPtrs[i], b, 0);

                var hdr = new WAVEHDR {
                    lpData         = _bufPins[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)BUFFER_SIZE
                };
                Marshal.StructureToPtr(hdr, _hdrPtrs[i], false);

                waveInPrepareHeader(_hWaveIn, _hdrPtrs[i], hdrSz);
                waveInAddBuffer    (_hWaveIn, _hdrPtrs[i], hdrSz);
            }

            waveInStart(_hWaveIn);

            _thread = new Thread(ProcessLoop) { IsBackground = true, Name = "WaveIn" };
            _thread.Start();
            Logger.Info("WaveInRecorder: started. " + NUM_BUFS + "x" + BUFFER_MS + "ms buffers.");
        }

        void CleanupDevice()
        {
            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < NUM_BUFS; i++) {
                try { if (_hWaveIn != IntPtr.Zero && _hdrPtrs[i] != IntPtr.Zero)
                    waveInUnprepareHeader(_hWaveIn, _hdrPtrs[i], hdrSz); } catch { }
                try { if (_hdrPtrs[i] != IntPtr.Zero) {
                    Marshal.FreeHGlobal(_hdrPtrs[i]); _hdrPtrs[i] = IntPtr.Zero; } } catch { }
                try { if (_bufPins[i].IsAllocated) _bufPins[i].Free(); } catch { }
            }
            if (_hWaveIn != IntPtr.Zero) { waveInClose(_hWaveIn); _hWaveIn = IntPtr.Zero; }
            if (_hEvent  != IntPtr.Zero) { CloseHandle(_hEvent);  _hEvent  = IntPtr.Zero; }
        }

        // ── Background buffer processor ───────────────────────────────────────

        void ProcessLoop()
        {
            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
            while (_running) {
                WaitForSingleObject(_hEvent, 200);
                if (!_running) break;

                for (int i = 0; i < NUM_BUFS; i++) {
                    if (_hdrPtrs[i] == IntPtr.Zero) continue;
                    var hdr = (WAVEHDR)Marshal.PtrToStructure(_hdrPtrs[i], typeof(WAVEHDR));
                    if ((hdr.dwFlags & WHDR_DONE) == 0) continue;

                    if (hdr.dwBytesRecorded > 0) {
                        var chunk = new byte[hdr.dwBytesRecorded];
                        Marshal.Copy(hdr.lpData, chunk, 0, chunk.Length);

                        // Always write to ring buffer (for pre-buffer)
                        lock (_ringLock)
                        {
                            for (int b = 0; b < chunk.Length; b++)
                            {
                                _ring[_ringHead] = chunk[b];
                                _ringHead = (_ringHead + 1) % _ring.Length;
                            }
                            _ringTotal += chunk.Length;
                        }

                        // Calculate Peak Level for UI
                        int currentPeak = 0;
                        for (int p = 0; p + 1 < chunk.Length; p += 2) {
                            int s = (short)(chunk[p] | (chunk[p+1] << 8));
                            if (s < 0) s = -s;
                            if (s > currentPeak) currentPeak = s;
                        }
                        CurrentPeakLevel = Math.Min(1f, currentPeak / 32767f);

                        // If actively capturing, also append to capture buffer
                        if (_capturing)
                        {
                            lock (_lock) { _pcm.AddRange(chunk); }
                        }
                    }

                    if (!_running) break;

                    hdr.dwBytesRecorded = 0;
                    hdr.dwFlags         = 0;
                    Marshal.StructureToPtr(hdr, _hdrPtrs[i], false);
                    waveInPrepareHeader(_hWaveIn, _hdrPtrs[i], hdrSz);
                    waveInAddBuffer    (_hWaveIn, _hdrPtrs[i], hdrSz);
                }
            }

            // Drain any final buffers
            for (int i = 0; i < NUM_BUFS; i++) {
                if (_hdrPtrs[i] == IntPtr.Zero) continue;
                var hdr = (WAVEHDR)Marshal.PtrToStructure(_hdrPtrs[i], typeof(WAVEHDR));
                if ((hdr.dwFlags & WHDR_DONE) != 0 && hdr.dwBytesRecorded > 0) {
                    var chunk = new byte[hdr.dwBytesRecorded];
                    Marshal.Copy(hdr.lpData, chunk, 0, chunk.Length);
                    if (_capturing) { lock (_lock) { _pcm.AddRange(chunk); } }
                }
            }
        }

        // ── WAV builder ───────────────────────────────────────────────────────
        static byte[] BuildWav(byte[] pcm)
        {
            using (var ms = new MemoryStream(44 + pcm.Length))
            using (var w  = new BinaryWriter(ms)) {
                w.Write(new char[]{'R','I','F','F'});
                w.Write((uint)(36 + pcm.Length));
                w.Write(new char[]{'W','A','V','E'});
                w.Write(new char[]{'f','m','t',' '});
                w.Write((uint)16);
                w.Write((ushort)1);
                w.Write((ushort)CHANNELS);
                w.Write((uint)SAMPLE_RATE);
                w.Write((uint)BYTES_PER_SEC);
                w.Write((ushort)(CHANNELS * BITS / 8));
                w.Write((ushort)BITS);
                w.Write(new char[]{'d','a','t','a'});
                w.Write((uint)pcm.Length);
                w.Write(pcm);
                return ms.ToArray();
            }
        }
    }
}

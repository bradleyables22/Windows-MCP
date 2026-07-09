using Server.InteropServices;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace Server.ScreenRecording
{
	internal sealed class MediaFoundationMp4Writer : IDisposable
	{
		private const int HundredNanosecondsPerSecond = 10_000_000;
		private static readonly Guid H264VideoFormat = new("34363248-0000-0010-8000-00AA00389B71");

		private readonly IMFSinkWriter sinkWriter;
		private readonly int streamIndex;
		private readonly long frameDuration;
		private readonly string path;
		private bool completed;
		private bool disposed;
		private int frameCount;

		public MediaFoundationMp4Writer(
			string path,
			int width,
			int height,
			int framesPerSecond,
			int videoBitrate)
		{
			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
			}

			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
			}

			if (framesPerSecond <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(framesPerSecond), framesPerSecond, "Frames per second must be positive.");
			}

			if (videoBitrate <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(videoBitrate), videoBitrate, "Video bitrate must be positive.");
			}

			this.path = Path.GetFullPath(path);
			frameDuration = HundredNanosecondsPerSecond / framesPerSecond;

			var directory = Path.GetDirectoryName(this.path);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			MediaFactory.MFStartup(useLightVersion: false);

			var attributes = MediaFactory.MFCreateAttributes(2);
			try
			{
				attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
				attributes.Set(SinkWriterAttributeKeys.DisableThrottling, 1u);
				sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(this.path, null, attributes);
			}
			finally
			{
				attributes.Dispose();
			}

			streamIndex = ConfigureStream(width, height, framesPerSecond, videoBitrate);
			sinkWriter.BeginWriting();
		}

		public int FrameCount => frameCount;

		public long BytesWritten => File.Exists(path) ? new FileInfo(path).Length : 0;

		public void WriteFrame(ScreenFrameResult frame)
		{
			if (disposed)
			{
				throw new ObjectDisposedException(nameof(MediaFoundationMp4Writer));
			}

			if (completed)
			{
				throw new InvalidOperationException("Cannot write frames after the MP4 file has been completed.");
			}

			using var buffer = MediaFactory.MFCreateMemoryBuffer(frame.Bgra32Bytes.Length);
			buffer.Lock(out var bufferPointer, out _, out _);
			try
			{
				Marshal.Copy(frame.Bgra32Bytes, 0, bufferPointer, frame.Bgra32Bytes.Length);
			}
			finally
			{
				buffer.Unlock();
			}

			buffer.CurrentLength = frame.Bgra32Bytes.Length;

			using var sample = MediaFactory.MFCreateSample();
			sample.SampleTime = frameCount * frameDuration;
			sample.SampleDuration = frameDuration;
			sample.AddBuffer(buffer);

			sinkWriter.WriteSample(streamIndex, sample);
			frameCount++;
		}

		public void Complete()
		{
			if (completed)
			{
				return;
			}

			sinkWriter.Finalize();
			completed = true;
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			try
			{
				if (!completed)
				{
					Complete();
				}
			}
			finally
			{
				sinkWriter.Dispose();
				MediaFactory.MFShutdown();
				disposed = true;
			}
		}

		private int ConfigureStream(
			int width,
			int height,
			int framesPerSecond,
			int videoBitrate)
		{
			using var outputType = MediaFactory.MFCreateMediaType();
			outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
			outputType.Set(MediaTypeAttributeKeys.Subtype, H264VideoFormat);
			outputType.Set(MediaTypeAttributeKeys.AvgBitrate, checked((uint)videoBitrate));
			outputType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
			outputType.Set(MediaTypeAttributeKeys.FrameSize, PackRatio(width, height));
			outputType.Set(MediaTypeAttributeKeys.FrameRate, PackRatio(framesPerSecond, 1));
			outputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));

			var configuredStreamIndex = sinkWriter.AddStream(outputType);

			using var inputType = MediaFactory.MFCreateMediaType();
			inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
			inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
			inputType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
			inputType.Set(MediaTypeAttributeKeys.FrameSize, PackRatio(width, height));
			inputType.Set(MediaTypeAttributeKeys.FrameRate, PackRatio(framesPerSecond, 1));
			inputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));
			inputType.Set(MediaTypeAttributeKeys.DefaultStride, checked((uint)(width * 4)));

			sinkWriter.SetInputMediaType(configuredStreamIndex, inputType, null);

			return configuredStreamIndex;
		}

		private static ulong PackRatio(int numerator, int denominator)
		{
			return ((ulong)(uint)numerator << 32) | (uint)denominator;
		}
	}
}

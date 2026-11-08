using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The thread that writes the files, so that the game never waits for storage. The main thread copies each frame into
	/// a buffer it borrows and puts it in a queue; this thread writes it, hands it to the operating system, changes files
	/// and their names, and gives the buffer back. Work is done in the order it came. What waits to be written is capped:
	/// a frame beyond the cap is dropped and counted, rather than memory growing. A WebGL Player has no threads and writes
	/// at once.
	/// </summary>
	internal sealed class TelemetryFileWriting
	{
		const int SmallestBuffer = 4 * 1024;

		enum Kind
		{
			Write,
			Rotate,
			FlushToDisk,
			Close,
			Barrier,
		}

		struct Work
		{
			internal Kind Kind;
			internal TelemetryFileFrames Frames;
			internal byte[] Buffer;
			internal int Length;
			internal ManualResetEventSlim Done;
		}

		readonly object m_Lock = new object();
		readonly Queue<Work> m_Queue = new Queue<Work>();
		readonly Dictionary<int, Stack<byte[]>> m_Free = new Dictionary<int, Stack<byte[]>>();
		readonly long m_MaximumPending;
		readonly bool m_Inline;
		long m_Pending;
		long m_FreeBytes;
		int m_Dropped;
		Exception m_Error;
		Thread m_Thread;
		bool m_Stopping;

		internal TelemetryFileWriting(long maximumPending)
		{
			m_MaximumPending = maximumPending;
#if UNITY_WEBGL && !UNITY_EDITOR
			m_Inline = true;
#endif
		}

		/// <summary>Borrows a buffer for a frame of this length, or none when the waiting frames are at the cap.</summary>
		internal byte[] Rent(int length)
		{
			var size = SmallestBuffer;
			while (size < length)
			{
				size *= 2;
			}
			lock (m_Lock)
			{
				// One frame larger than the cap still goes out when nothing else waits.
				if (m_Pending > 0 && m_Pending + size > m_MaximumPending)
				{
					m_Dropped++;
					return null;
				}
				m_Pending += size;
				if (m_Free.TryGetValue(size, out var free) && free.Count > 0)
				{
					m_FreeBytes -= size;
					return free.Pop();
				}
			}
			return new byte[size];
		}

		internal void Write(TelemetryFileFrames frames, byte[] buffer, int length)
		{
			Post(new Work { Kind = Kind.Write, Frames = frames, Buffer = buffer, Length = length });
		}

		internal void Rotate(TelemetryFileFrames frames)
		{
			Post(new Work { Kind = Kind.Rotate, Frames = frames });
		}

		internal void FlushToDisk(TelemetryFileFrames frames)
		{
			Post(new Work { Kind = Kind.FlushToDisk, Frames = frames });
		}

		internal void Close(TelemetryFileFrames frames)
		{
			Post(new Work { Kind = Kind.Close, Frames = frames });
		}

		/// <summary>Waits until what was asked before is done. Only for the calls that promise it: flush, seal, stop, pause.</summary>
		/// <returns>False when it took longer than the timeout.</returns>
		internal bool Wait(TimeSpan timeout)
		{
			if (m_Inline)
			{
				return true;
			}
			using (var done = new ManualResetEventSlim(false))
			{
				Post(new Work { Kind = Kind.Barrier, Done = done });
				return done.Wait(timeout);
			}
		}

		/// <summary>Finishes the work and ends the thread.</summary>
		internal void Stop(TimeSpan timeout)
		{
			Thread thread;
			lock (m_Lock)
			{
				m_Stopping = true;
				thread = m_Thread;
				Monitor.Pulse(m_Lock);
			}
			thread?.Join(timeout);
		}

		/// <summary>The first failure of a write, once. The main thread takes it to end the output.</summary>
		internal Exception TakeError()
		{
			lock (m_Lock)
			{
				var error = m_Error;
				m_Error = null;
				return error;
			}
		}

		/// <summary>The frames dropped at the cap since the last call.</summary>
		internal int TakeDropped()
		{
			lock (m_Lock)
			{
				var dropped = m_Dropped;
				m_Dropped = 0;
				return dropped;
			}
		}

		void Post(in Work work)
		{
			if (m_Inline)
			{
				Do(work);
				return;
			}
			lock (m_Lock)
			{
				m_Queue.Enqueue(work);
				if (m_Thread == null)
				{
					m_Thread = new Thread(Loop) { IsBackground = true, Name = "Dexc Telemetry files" };
					m_Thread.Start();
				}
				Monitor.Pulse(m_Lock);
			}
		}

		void Loop()
		{
			while (true)
			{
				Work work;
				lock (m_Lock)
				{
					while (m_Queue.Count == 0)
					{
						if (m_Stopping)
						{
							m_Thread = null;
							return;
						}
						Monitor.Wait(m_Lock);
					}
					work = m_Queue.Dequeue();
				}
				Do(work);
			}
		}

		void Do(in Work work)
		{
			try
			{
				switch (work.Kind)
				{
					case Kind.Write:
						if (!HasFailed())
						{
							work.Frames.WriteNow(work.Buffer, work.Length);
						}
						break;
					case Kind.Rotate:
						if (!HasFailed())
						{
							work.Frames.RotateNow();
						}
						break;
					case Kind.FlushToDisk:
						if (!HasFailed())
						{
							work.Frames.FlushToDiskNow();
						}
						break;
					case Kind.Close:
						work.Frames.CloseNow();
						break;
					case Kind.Barrier:
						work.Done.Set();
						break;
				}
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				lock (m_Lock)
				{
					m_Error = m_Error ?? exception;
				}
			}
			finally
			{
				if (work.Buffer != null)
				{
					GiveBack(work.Buffer);
				}
			}
		}

		bool HasFailed()
		{
			lock (m_Lock)
			{
				return m_Error != null;
			}
		}

		/// <summary>Keeps the buffer for the next frame, up to as many bytes as may wait, so that the pool stays bounded too.</summary>
		void GiveBack(byte[] buffer)
		{
			lock (m_Lock)
			{
				m_Pending -= buffer.Length;
				if (m_FreeBytes + buffer.Length > m_MaximumPending)
				{
					return;
				}
				if (!m_Free.TryGetValue(buffer.Length, out var free))
				{
					free = new Stack<byte[]>();
					m_Free.Add(buffer.Length, free);
				}
				free.Push(buffer);
				m_FreeBytes += buffer.Length;
			}
		}
	}
}

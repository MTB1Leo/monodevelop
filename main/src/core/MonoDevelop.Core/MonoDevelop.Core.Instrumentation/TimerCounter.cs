// 
// TimerCounter.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Threading;

namespace MonoDevelop.Core.Instrumentation
{
	[Serializable]
	public class TimerCounter : Counter
	{
		[NonSerialized]
		ITimeCounter lastTimer;
		double minSeconds;
		TimeSpan totalTime;
		int totalCountWithTime;
		TimeSpan minTime = TimeSpan.MaxValue;
		TimeSpan maxTime;

		public TimerCounter (string name, CounterCategory category) : base (name, category)
		{
		}

		public override string ToString ()
		{
			return string.Format ("[TimerCounter: Name={0} Id={1} Category={2} MinSeconds={3}, TotalTime={4}, AverageTime={5}, MinTime={6}, MaxTime={7}, CountWithDuration={8}]", Name, Id, Category, MinSeconds, TotalTime, AverageTime, MinTime, MaxTime, CountWithDuration);
		}

		public double MinSeconds {
			get { return this.minSeconds; }
			set { this.minSeconds = value; }
		}

		public TimeSpan TotalTime {
			get { return totalTime; }
		}

		public TimeSpan AverageTime {
			get { return totalCountWithTime > 0 ? new TimeSpan (totalTime.Ticks / totalCountWithTime) : TimeSpan.FromTicks (0); }
		}

		public TimeSpan MinTime {
			get { return totalCountWithTime > 0 ? this.minTime : TimeSpan.Zero; }
		}

		public TimeSpan MaxTime {
			get { return this.maxTime; }
		}

		public int CountWithDuration {
			get { return totalCountWithTime; }
		}

		internal void AddTime (TimeSpan time)
		{
			lock (values) {
				totalCountWithTime++;
				totalTime += time;
				if (time < minTime)
					minTime = time;
				if (time > maxTime)
					maxTime = time;
			}
		}

		public override void Trace (string message)
		{
			if (Enabled) {
				if (lastTimer != null)
					lastTimer.Trace (message);
				else {
					lock (values) {
						StoreValue (message, null, null);
					}
				}
			} else if (LogMessages) {
				if (lastTimer != null)
					lastTimer.Trace (message);
				else if (message != null)
					InstrumentationService.LogMessage (message);
			}
		}

		public ITimeTracker BeginTiming ()
		{
			return BeginTiming (null, null);
		}

		public ITimeTracker BeginTiming (string message)
		{
			return BeginTiming (message, null);
		}

		public ITimeTracker BeginTiming (IDictionary<string, string> metadata)
		{
			return BeginTiming (null, metadata);
		}

		public ITimeTracker BeginTiming (string message, IDictionary<string, string> metadata)
		{
			return BeginTiming (message, metadata != null ? new CounterMetadata (metadata) : null, CancellationToken.None);
		}

		internal ITimeTracker<T> BeginTiming<T> (string message, T metadata, CancellationToken cancellationToken) where T : CounterMetadata
		{
			ITimeTracker<T> timer;
			if (!Enabled && !LogMessages) {
				timer = new DummyTimerCounter<T> (metadata);
			} else {
				var c = new TimeCounter<T> (this, metadata, cancellationToken);
				if (Enabled) {
					lock (values) {
						lastTimer = c;
						timer = c;
						count++;
						totalCount++;
						int i = StoreValue (message, lastTimer, metadata?.Properties);
						lastTimer.TraceList.ValueIndex = i;
					}
				} else {
					if (message != null)
						InstrumentationService.LogMessage (message);
					else
						InstrumentationService.LogMessage ("START: " + Name);
					lastTimer = c;
					timer = c;
				}
			}
			return timer;
		}

		public void EndTiming ()
		{
			if (lastTimer != null) {
				lastTimer.End ();
				lastTimer = null;
			}
		}
	}

	public class TimerCounter<T> : TimerCounter where T : CounterMetadata, new()
	{
		public TimerCounter (string name, CounterCategory category) : base (name, category)
		{
		}

		new public ITimeTracker<T> BeginTiming ()
		{
			return base.BeginTiming<T> (null, new T (), CancellationToken.None);
		}

		public ITimeTracker<T> BeginTiming (CancellationToken cancellationToken)
		{
			return base.BeginTiming<T> (null, new T (), cancellationToken);
		}

		public ITimeTracker<T> BeginTiming (T metadata)
		{
			return base.BeginTiming<T> (null, metadata, CancellationToken.None);
		}

		public ITimeTracker<T> BeginTiming (T metadata, CancellationToken cancellationToken)
		{
			return base.BeginTiming<T> (null, metadata, cancellationToken);
		}

		new public ITimeTracker<T> BeginTiming (string message)
		{
			return base.BeginTiming<T> (message, new T (), CancellationToken.None);
		}

		public ITimeTracker<T> BeginTiming (string message, CancellationToken cancellationToken)
		{
			return base.BeginTiming<T> (message, new T (), cancellationToken);
		}

		public ITimeTracker<T> BeginTiming (string message, T metadata)
		{
			return base.BeginTiming<T> (message, metadata, CancellationToken.None);
		}

		public ITimeTracker<T> BeginTiming (string message, T metadata, CancellationToken cancellationToken)
		{
			return base.BeginTiming<T> (message, metadata, cancellationToken);
		}
	}

	public class CounterMetadata
	{
		IDictionary<string, string> properties;

		internal protected IDictionary<string, string> Properties => properties;

		public CounterMetadata ()
		{
			properties = new Dictionary<string, string> ();
		}

		public CounterMetadata (IDictionary<string, string> properties)
		{
			this.properties = properties;
		}

		public CounterResult Result {
			get {
				if (!properties.TryGetValue ("Result", out var result) || !Enum.TryParse (result, out CounterResult res))
					return CounterResult.Unspecified;
				return res;
			}
			set {
				if (value == CounterResult.Unspecified)
					properties.Remove ("Result");
				else
					properties ["Result"] = value.ToString ();
			}
		}

		public void SetUserFault ()
		{
			properties ["Result"] = "UserFault";
		}
	}

	public enum CounterResult
	{
		Unspecified,
		Success,
		Failure,
		UserCancel,
		UserFault
	}

}


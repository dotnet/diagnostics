// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers
{
    internal sealed class SlidingWindow
    {
        //Any events that occur within this interval are merged.
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
        private readonly LinkedList<(DateTime Timestamp, int Count)> _timeData = new();
        private readonly TimeSpan _window;

        public SlidingWindow(TimeSpan slidingWindow)
        {
            _window = slidingWindow;
        }

        public int Count { get; private set; }

        public void AddDataPoint(DateTime timestamp)
        {
            //ASSUMPTION! We are always expecting to get events that are equal or increasing in time.
            if (_timeData.Last == null)
            {
                _timeData.AddLast((timestamp, 1));
                Count++;
                return;
            }

            (DateTime lastTimestamp, int lastCount) = _timeData.Last.Value;

            Debug.Assert(timestamp >= lastTimestamp, "Unexpected timestamp");

            //Coalesce close points together
            if (timestamp - lastTimestamp < _interval)
            {
                _timeData.Last.Value = (lastTimestamp, lastCount + 1);
                Count++;
                //No need for further processing since we can't fall out of the sliding window.
                return;
            }

            _timeData.AddLast((timestamp, 1));
            Count++;

            while (_timeData.First != null)
            {
                (DateTime firstTimestamp, int firstCount) = _timeData.First.Value;
                if (timestamp - firstTimestamp > _window)
                {
                    _timeData.RemoveFirst();
                    Count -= firstCount;
                }
                else
                {
                    break;
                }
            }
        }

        public void Clear()
        {
            _timeData.Clear();
            Count = 0;
        }
    }
}

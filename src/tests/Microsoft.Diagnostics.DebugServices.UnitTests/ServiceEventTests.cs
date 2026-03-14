// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    /// <summary>
    /// Test the service event implementation
    /// </summary>
    public class ServiceEventTests
    {
        [Fact]
        public void TestAddAndFire()
        {
            ServiceEvent event1 = new();

            int callback1Fired = 0;
            int callback2Fired = 0;
            int callback3Fired = 0;

            IDisposable unregister1 = event1.Register(() => callback1Fired++);
            event1.Fire();
            Assert.Equal(1, callback1Fired);

            IDisposable unregister2 = event1.Register(() => callback2Fired++);
            IDisposable unregister3 = event1.Register(() => callback3Fired++);

            // Make sure all of the callbacks fire
            callback1Fired = 0;
            callback2Fired = 0;
            callback3Fired = 0;
            event1.Fire();
            Assert.Equal(1, callback1Fired);
            Assert.Equal(1, callback2Fired);
            Assert.Equal(1, callback3Fired);

            // Remove/unregister one of the callbacks
            unregister2.Dispose();

            // Make sure callback #2 doesn't fire
            callback1Fired = 0;
            callback2Fired = 0;
            callback3Fired = 0;
            event1.Fire();
            Assert.Equal(1, callback1Fired);
            Assert.Equal(0, callback2Fired);
            Assert.Equal(1, callback3Fired);

            // Now remove all of them and double disposing #2
            unregister1.Dispose();
            unregister2.Dispose();
            unregister3.Dispose();

            // Make sure none of them fire
            callback1Fired = 0;
            callback2Fired = 0;
            callback3Fired = 0;
            event1.Fire();
            Assert.Equal(0, callback1Fired);
            Assert.Equal(0, callback2Fired);
            Assert.Equal(0, callback3Fired);
        }

        private IDisposable _unregister3;

        [Fact]
        public void TestRemoveInCallback()
        {
            ServiceEvent event1 = new();

            int callback1Fired = 0;
            int callback2Fired = 0;
            int callback3Fired = 0;

            // Test removing the callback in the callback (oneshot)
            IDisposable unregister1 = event1.Register(() => callback1Fired++);
            IDisposable unregister2 = event1.Register(() => callback2Fired++);
            _unregister3 = event1.Register(() => {
                callback3Fired++;
                _unregister3.Dispose();
            });
            event1.Fire();
            Assert.Equal(1, callback1Fired);
            Assert.Equal(1, callback2Fired);
            Assert.Equal(1, callback3Fired);

            unregister1.Dispose();
            unregister2.Dispose();

            // Remove the test of them and make none of them fire
            callback1Fired = 0;
            callback2Fired = 0;
            callback3Fired = 0;
            event1.Fire();
            Assert.Equal(0, callback1Fired);
            Assert.Equal(0, callback2Fired);
            Assert.Equal(0, callback3Fired);
            _unregister3 = null;
        }
    }
}

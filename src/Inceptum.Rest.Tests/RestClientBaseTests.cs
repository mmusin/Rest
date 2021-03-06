﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Routing;
using System.Web.Http.SelfHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace Inceptum.Rest.Tests
{
    [TestFixture]
    public class RestClientBaseTests
    {
        private HttpSelfHostServer[] m_Servers;
        private const int SERVERS_COUNT = 3;

        [SetUp]
        public void FixtureSetUp()
        {
            TestController.FailingPorts.Clear();
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            m_Servers = Enumerable.Range(1, SERVERS_COUNT).Select(i => new HttpSelfHostServer(createApiConfiguration("http://localhost:" + (1000 + i)))).ToArray();
            Task.WaitAll(m_Servers.Select(s => s.OpenAsync()).ToArray());
            Task.WaitAll(
            Enumerable.Range(1, SERVERS_COUNT)
                .Select(i => "http://localhost:" + (1000 + i))
                .Select(uri => new RestClient(new[] { uri }))
                .Select(c =>
                {
                    var r = c.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None);
                    c.Dispose();
                    return r;
                }).ToArray());
            Console.WriteLine("==============================================");
        }

        [TearDown]
        public void FixtureTearDown()
        {
            Task.WaitAll(m_Servers.Select(s => s.CloseAsync()).ToArray());
        }

        private static HttpSelfHostConfiguration createApiConfiguration(string baseUrl)
        {
            var config = new HttpSelfHostConfiguration(baseUrl);

            JsonMediaTypeFormatter jsonFormatter = config.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            jsonFormatter.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Ignore;
            jsonFormatter.SerializerSettings.Formatting = Formatting.Indented;
            jsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Routes.MapHttpRoute("Default", "api/{controller}", new { controller = "Test" });
            config.MapHttpAttributeRoutes();

            return config;
        }

        [Test]
        public async void PerformanceTest()
        {
            using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray(), delayTimeout: 0))
            {
                Console.WriteLine(await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None));

                Stopwatch sw = Stopwatch.StartNew();

                var tasks = Enumerable.Range(1, 1000)
                    .Select(i =>
                    {
                        Thread.Sleep(0);
                        return testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None);

                    })
                    .ToArray();
                Console.WriteLine(Thread.CurrentThread.ManagedThreadId);
                Console.WriteLine(sw.ElapsedMilliseconds);
                Task.WaitAll(tasks);
                Console.WriteLine(sw.ElapsedMilliseconds);

                var ports = tasks.Select(t => t.Result).Where(t => t != null);
                var dict = new Dictionary<string, int> { { "1001", 0 }, { "1002", 0 }, { "1003", 0 } };
                foreach (var port in ports)
                {
                    dict[port.Response]++;
                }

                foreach (var pair in dict.OrderBy(p => p.Key))
                {
                    Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
                }
            }
        }

        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public async void RequestUriShouldBeRelativeTest()
        {
            using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray(), farmRequestTimeout: 200, delayTimeout: 1))
            {
                for (var j = 1; j <= SERVERS_COUNT; j++)
                {
                    TestController.FailingPorts.Add(1000 + j);
                }
                var i = 0;
                await testRestClient.SendAsync<string>(() =>
                    new HttpRequestMessage(HttpMethod.Get, i++ == 0
                        ? new Uri("/ok", UriKind.Relative)
                        : new Uri("http://localhost:1001/ok", UriKind.RelativeOrAbsolute)),
                    CultureInfo.CurrentUICulture);
            }
        }

        [Test]
        public async void TimeoutExceptionShouldContainAllAttemptsInformationTest()
        {
            var cnt = 0;
            FarmRequestTimeoutException ex = null;
            try
            {
                using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray(), farmRequestTimeout: 200))
                {
                    for (var j = 1; j <= SERVERS_COUNT; j++)
                    {
                        TestController.FailingPorts.Add(1000 + j);
                    }

                    await testRestClient.SendAsync<string>(() =>
                    {
                        cnt++;
                        return new HttpRequestMessage(HttpMethod.Get, new Uri("/ok", UriKind.Relative));
                    }, CultureInfo.CurrentUICulture);

                }
            }
            catch (FarmRequestTimeoutException e)
            {
                ex = e;
            }
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.Attempts.Count(), Is.EqualTo(cnt));
            Assert.That(ex.Attempts.Select(a => a.Response.StatusCode), Is.All.EqualTo(HttpStatusCode.InternalServerError));
            Console.WriteLine(ex);
        }

        [Test]
        public void AddressesSelectionTest()
        {
            using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray()))
            {
                TestController.FailingPorts.Add(1001);
                var tasks = Enumerable.Range(1, 100)
                    .Select(i =>
                    {
                        Thread.Sleep(10);
                        return testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None);

                    })
                    .ToArray();
                Task.WaitAll(tasks);

                var ports = tasks.Select(t => t.Result).Where(t => t != null);
                var dict = new Dictionary<string, int> { { "1001", 0 }, { "1002", 0 }, { "1003", 0 } };
                foreach (var port in ports)
                {
                    dict[port.Response]++;
                }

                foreach (var pair in dict.OrderBy(p => p.Key))
                {
                    Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
                }
            }
        }

        [Test]
        public async void RequestTillTimeoutEndsTest()
        {
            TestController.FailingPorts.AddRange(new[] { 1001, 1002, 1003 });
            var addresses = Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray();
            using (var testRestClient = new RestClient(addresses, farmRequestTimeout: 1500, delayTimeout: 1))
            {
                var e = new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(state =>
                {
                    e.WaitOne();
                    Thread.Sleep(1000);
                    TestController.FailingPorts.Clear();
                });
                var sw = Stopwatch.StartNew();
                e.Set();
                await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None);
                sw.Stop();
                Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(900));
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500));
            }
        }

        [Test]
        public async void FarmRequestTimeoutExceptionIsThrownOnFarmRequestTimeoutReachedTest()
        {
            TestController.FailingPorts.AddRange(new[] { 1001, 1002, 1003 });
            var addresses = Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray();
            using (var testRestClient = new RestClient(addresses, farmRequestTimeout: 600))
            {
                var sw = Stopwatch.StartNew();
                FarmRequestTimeoutException ex = null;
                try
                {
                    await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None);
                }
                catch (FarmRequestTimeoutException e)
                {
                    ex = e;
                }
                Assert.That(ex, Is.Not.Null);
                sw.Stop();
                Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(600));
                Console.WriteLine(ex);
            }
        }

        [Test]
        public async void CanCancelPendingRequestTest()
        {
            var addresses = Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray();
            const int nodeDelay = 5 * 1000;
            using (var testRestClient = new RestClient(addresses, singleAddressTimeout: nodeDelay))
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(2.5 * nodeDelay)); // Should try at least two uris before cancellation

                FarmRequestTimeoutException expectedException = null;
                try
                {
                    await testRestClient.GetData(new Uri("/delay?seconds=60", UriKind.Relative), CultureInfo.CurrentUICulture, cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (FarmRequestTimeoutException ex)
                {
                    expectedException = ex;
                }

                Assert.IsNotNull(expectedException);
                Assert.AreEqual("Request was cancelled by consuming code", expectedException.Message);
                Console.WriteLine(expectedException);
            }
        }

        [Test]
        [Timeout(10 * 1000)]
        public async void ShouldNotFailUrlIfConsumingCodeRequestedCancellation()
        {
            var addresses = new[] { "http://localhost:1001" };

            using (var testRestClient = new RestClient(addresses, singleAddressTimeout: 1000 * 60, delayTimeout: 1000 * 1000))
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000 * 3));

                Exception expectedException = null;
                try
                {
                    await testRestClient.GetData(new Uri("/delay?seconds=15", UriKind.Relative), CultureInfo.CurrentUICulture, cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    expectedException = e;
                }

                Assert.IsNotNull(expectedException);
                Assert.IsTrue(expectedException.InnerException is OperationCanceledException);

                await testRestClient.GetData(new Uri("/delay?seconds=1", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
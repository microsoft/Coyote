// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Microsoft.Coyote.Telemetry
{
    /// <summary>
    /// This class wraps the GA4 protocol.
    /// See https://developers.google.com/analytics/devguides/collection/protocol/ga4/reference.
    /// </summary>
    [KnownType(typeof(PageMeasurement))]
    [KnownType(typeof(EventMeasurement))]
    [KnownType(typeof(ExceptionMeasurement))]
    [KnownType(typeof(UserTimingMeasurement))]
    [KnownType(typeof(TestEventMeasurement))]
    [KnownType(typeof(UserProperties))]
    [KnownType(typeof(UserPropertyValue))]
    [DataContract]
    internal class Analytics
    {
        public Analytics()
        {
            this.TimeStamp = DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000;
        }

        [IgnoreDataMember]
        public string ApiSecret { get; set; }

        [IgnoreDataMember]
        public string MeasurementId { get; set; }

        [DataMember(Name = "client_id", Order = 1)]
        public string ClientId { get; set; }

        [DataMember(Name = "user_id", Order = 2)]
        public string UserId { get; set; }

        [DataMember(Name = "timestamp_micros", Order = 3)]
        public long TimeStamp { get; set; }

        [DataMember(Name = "non_personalized_ads", Order = 3)]
        public bool NonPersonalizedAds { get; set; }

        [DataMember(Name = "user_properties", Order = 4)]
        public UserProperties UserProperties { get; set; }

        [DataMember(Name = "events", Order = 5)]
        public List<Measurement> Events = new List<Measurement>();

        public string ToQueryString()
        {
            Required(this.MeasurementId, "MeasurementId");
            Required(this.ClientId, "ClientId");
            Required(this.ApiSecret, "ApiSecret");
            return string.Format("api_secret={0}&measurement_id={1}", this.ApiSecret, this.MeasurementId);
        }

        protected static void Required(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(name);
            }
        }
    }

    [DataContract]
    internal class UserProperties
    {
        [DataMember(Name = "platform")]
        public UserPropertyValue Platform { get; set; }
        [DataMember(Name = "platform_version")]
        public UserPropertyValue PlatformVersion { get; set; }
        [DataMember(Name = "dotnet")]
        public UserPropertyValue FrameworkVersion { get; set; }
        [DataMember(Name = "language")]
        public UserPropertyValue Language { get; set; }
    }

    [DataContract]
    internal class UserPropertyValue
    {
        public UserPropertyValue(string value)
        {
            this.Value = value;
        }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal abstract class Measurement
    {
        public Measurement()
        {
            this.Version = 1;
        }

        protected int Version { get; set; }

        [DataMember(Name = "name")]
        protected string Name { get; set; }

        [DataMember(Name = "params")]
        public Dictionary<string, object> Params = new Dictionary<string, object>();

        protected string GetParam(string name)
        {
            if (this.Params.TryGetValue(name, out object value) && value is string s)
            {
                return s;
            }

            return null;
        }

        protected void SetParam(string name, string value)
        {
            this.Params[name] = value;
        }

        protected double GetDoubleParam(string name)
        {
            if (this.Params.TryGetValue(name, out object value) && value is double d)
            {
                return d;
            }

            return 0;
        }

        protected void SetDoubleParam(string name, double value)
        {
            this.Params[name] = value;
        }
    }

    [DataContract]
    internal class PageMeasurement : Measurement
    {
        public PageMeasurement()
        {
            this.Name = "page_view";
        }

        public string HostName
        {
            get => this.GetParam("host_name");
            set => this.SetParam("host_name", value);
        }

        public string Path
        {
            get => this.GetParam("page_location");
            set => this.SetParam("page_location", value);
        }

        public string Title
        {
            get => this.GetParam("page_title");
            set => this.SetParam("page_title", value);
        }

        public string Referrer
        {
            get => this.GetParam("page_referrer");
            set => this.SetParam("page_referrer", value);
        }

        public string UserAgent
        {
            get => this.GetParam("user_agent");
            set => this.SetParam("user_agent", value);
        }
    }

    [DataContract]
    internal class EventMeasurement : Measurement
    {
        public EventMeasurement()
        {
            this.Name = "event";
        }

        public string Category
        {
            get => this.GetParam("category");
            set => this.SetParam("category", value);
        }

        public string Action
        {
            get => this.GetParam("action");
            set => this.SetParam("action", value);
        }

        public string Label
        {
            get => this.GetParam("label");
            set => this.SetParam("label", value);
        }

        public string Value
        {
            get => this.GetParam("value");
            set => this.SetParam("value", value);
        }
    }

    [DataContract]
    internal class ExceptionMeasurement : Measurement
    {
        public ExceptionMeasurement()
        {
            this.Name = "exception";
        }

        public string Description
        {
            get => this.GetParam("description");
            set => this.SetParam("description", value);
        }

        public string Fatal
        {
            get => this.GetParam("fatal");
            set => this.SetParam("fatal", value);
        }
    }

    [DataContract]
    internal class UserTimingMeasurement : Measurement
    {
        public UserTimingMeasurement()
        {
            this.Name = "timing";
        }

        public string Category
        {
            get => this.GetParam("category");
            set => this.SetParam("category", value);
        }

        public string Variable
        {
            get => this.GetParam("variable");
            set => this.SetParam("variable", value);
        }

        public string Time
        {
            get => this.GetParam("time");
            set => this.SetParam("time", value);
        }

        public string Label
        {
            get => this.GetParam("label");
            set => this.SetParam("label", value);
        }
    }

    [DataContract]
    internal class TestEventMeasurement : Measurement
    {
        public TestEventMeasurement()
        {
            this.Name = "event";
        }

        public string Coyote
        {
            get => this.GetParam("coyote");
            set => this.SetParam("coyote", value);
        }

        public string Action
        {
            get => this.GetParam("action");
            set => this.SetParam("action", value);
        }

        public string Result
        {
            get => this.GetParam("value");
            set => this.SetParam("value", value);
        }

        public int Bugs
        {
            get => (int)this.GetDoubleParam("bugs");
            set => this.SetDoubleParam("bugs", value);
        }

        public double TestTime
        {
            get => (int)this.GetDoubleParam("test_time");
            set => this.SetDoubleParam("test_time", value);
        }
    }
}

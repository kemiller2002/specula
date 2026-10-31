module Specula.Tests.TimeTests

open System
open Xunit
open Specula.Engine
open Specula.Tests.Fixtures

let zone = TimeZoneInfo.CreateCustomTimeZone("test-minus-4", TimeSpan.FromHours -4.0, "Test", "Test")

[<Fact>]
let ``local formatting shows the offset; UTC is preserved`` () =
    let instant = t "2026-10-30T22:49:55Z"
    Assert.Equal("2026-10-30 18:49 UTC−04:00", Time.local zone instant)
    Assert.Equal("2026-10-30T22:49:55Z", Time.utc instant)
    Assert.Equal("2026-10-30 22:49 UTC", Time.local TimeZoneInfo.Utc instant)

[<Fact>]
let ``datetime-local input is interpreted in the viewer's zone`` () =
    Assert.Equal(Some(t "2026-10-30T22:00:00Z"), Time.parseLocalInput zone "2026-10-30T18:00")
    Assert.Equal("2026-10-30T18:00", Time.toLocalInput zone (t "2026-10-30T22:00:00Z"))
    Assert.Equal(None, Time.parseLocalInput zone "not a date")

[<Fact>]
let ``window around a UTC kickoff crossing local midnight`` () =
    let kickoff = t "2026-10-31T02:30:00Z" // 22:30 local the previous day
    let start = (Time.parseLocalInput zone "2026-10-30T22:00").Value
    Assert.True(kickoff >= start)
    Assert.Equal("2026-10-30T22:30", Time.toLocalInput zone kickoff)

[<Fact>]
let ``durations and median`` () =
    Assert.Equal("40m", Time.duration (TimeSpan.FromMinutes 40.0))
    Assert.Equal("2h 5m", Time.duration (TimeSpan.FromMinutes 125.0))
    Assert.Equal(Some(TimeSpan.FromMinutes 15.0), Time.median [ TimeSpan.FromMinutes 10.0; TimeSpan.FromMinutes 20.0 ])
    Assert.Equal(None, Time.median [])

namespace ActualChat.Media.UnitTests;

public class MapLinkParserTest
{
    [Theory]
    [InlineData("https://www.google.com/maps?q=48.85837,2.294481", 48.85837, 2.294481)]
    [InlineData("https://maps.google.com/?q=loc:48.85837,2.294481", 48.85837, 2.294481)]
    [InlineData("https://www.google.com/maps/@48.85837,2.294481,17z", 48.85837, 2.294481)]
    [InlineData("https://www.google.de/maps/search/?api=1&query=48.85837%2C2.294481", 48.85837, 2.294481)]
    [InlineData("https://www.google.com/maps/search/48.85837,+2.294481", 48.85837, 2.294481)]
    [InlineData("https://www.google.com/maps/dir/?api=1&destination=-33.8568,151.2153", -33.8568, 151.2153)]
    [InlineData(
        "https://www.google.com/maps/place/Eiffel+Tower/@48.8560,2.2900,17z/data=!3m1!4b1!4m6!3m5"
        + "!1s0x47e66e2964e34e2d:0x8ddca9ee380ef7e0!8m2!3d48.8583701!4d2.2944813",
        48.8583701, 2.2944813)]
    [InlineData(
        "https://consent.google.com/m?continue=https%3A%2F%2Fwww.google.com%2Fmaps%2F%4048.85837%2C2.294481%2C17z",
        48.85837, 2.294481)]
    [InlineData("https://maps.apple.com/?ll=48.85837,2.294481&q=Eiffel", 48.85837, 2.294481)]
    [InlineData("https://maps.apple.com/place?coordinate=48.85837%2C2.294481&name=Eiffel", 48.85837, 2.294481)]
    [InlineData("https://www.openstreetmap.org/?mlat=48.85837&mlon=2.294481#map=15/48.9/2.3", 48.85837, 2.294481)]
    [InlineData("https://www.openstreetmap.org/#map=15/48.85837/2.294481", 48.85837, 2.294481)]
    [InlineData("https://yandex.com/maps/?ll=2.3,48.9&pt=2.294481,48.85837,pm2rdm&z=15", 48.85837, 2.294481)]
    [InlineData("https://maps.yandex.ru/?ll=2.294481%2C48.85837&z=15", 48.85837, 2.294481)]
    [InlineData(
        "https://yandex.ru/maps/101516/lappeenranta/?ll=28.195655%2C61.047877&mode=poi"
        + "&poi%5Bpoint%5D=28.195304%2C61.048040&poi%5Buri%5D=ymapsbm1%3A%2F%2Forg%3Foid%3D183690196133&z=19.14",
        61.04804, 28.195304)]
    [InlineData(
        "https://yandex.com/maps/?ll=2.3%2C48.9&mode=whatshere&whatshere%5Bpoint%5D=2.294481%2C48.85837&z=15",
        48.85837, 2.294481)]
    [InlineData("https://www.bing.com/maps?cp=48.85837~2.294481&lvl=16", 48.85837, 2.294481)]
    [InlineData("https://ul.waze.com/ul?ll=48.85837%2C2.294481&navigate=yes", 48.85837, 2.294481)]
    [InlineData("https://2gis.ru/moscow/geo/37.617635,55.755814", 55.755814, 37.617635)]
    [InlineData("https://2gis.ae/dubai?m=55.2708%2C25.2048%2F16", 25.2048, 55.2708)]
    public void MapLinkShouldGiveItsPoint(string url, double latitude, double longitude)
    {
        // act
        var target = MapLinkParser.TryParse(url);

        // assert
        target.Should().NotBeNull();
        target!.Point.Should().Be(new GeoPoint(latitude, longitude));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("geo:48.85837,2.294481")]
    [InlineData("https://example.com/maps?q=48.85837,2.294481")]
    [InlineData("https://www.google.com/search?q=48.85837,2.294481")]
    [InlineData("https://www.google.com/maps?q=Eiffel+Tower,+Paris")]
    [InlineData("https://www.google.com/maps/place/Eiffel+Tower/data=!4m2!3m1!1s0x47e66e2964e34e2d:0x8ddca9ee380ef7e0")]
    [InlineData("https://www.google.com/maps?q=148.85837,2.294481")]
    [InlineData("https://maps.app.goo.gl/AbCdEf123")]
    [InlineData("https://consent.google.com/m?continue=https%3A%2F%2Fconsent.google.com%2Fm")]
    public void OtherLinkShouldGiveNoPoint(string url)
    {
        // act
        var target = MapLinkParser.TryParse(url);

        // assert
        target.Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.google.com/maps/place/Eiffel+Tower/@48.8560,2.2900,17z", "Eiffel Tower")]
    [InlineData(
        "https://www.google.ru/maps/place/Maunulan+vanha+hyppyritorni/@60.2224341,24.9222003,16.71z/data=!4m6",
        "Maunulan vanha hyppyritorni")]
    [InlineData("https://www.google.com/maps/place/Caf%C3%A9+de+Flore/@48.8541,2.3326,17z", "Café de Flore")]
    [InlineData(
        "https://consent.google.com/m?continue=https%3A%2F%2Fwww.google.com%2Fmaps%2Fplace%2FLouvre%2F%4048.86%2C2.33",
        "Louvre")]
    [InlineData("https://maps.apple.com/?ll=48.85837,2.294481&q=Eiffel+Tower", "Eiffel Tower")]
    [InlineData("https://maps.apple.com/place?coordinate=48.85837%2C2.294481&name=Eiffel", "Eiffel")]
    public void MapLinkShouldGiveItsPointName(string url, string name)
    {
        // act
        var target = MapLinkParser.TryParse(url);

        // assert
        target.Should().NotBeNull();
        target!.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/maps/place/Eiffel+Tower/@48.8560,2.2900,17z")]
    [InlineData("https://www.google.com/maps/@48.85837,2.294481,17z")]
    [InlineData("https://www.google.com/maps/place/48.85837,2.294481/@48.85837,2.294481,17z")]
    [InlineData("https://www.google.com/maps/place/48%C2%B051'30.1%22N+2%C2%B017'40.1%22E/@48.85837,2.294481,17z")]
    [InlineData("https://maps.apple.com/?q=48.85837,2.294481")]
    [InlineData("https://www.openstreetmap.org/?mlat=48.85837&mlon=2.294481")]
    public void OtherLinkShouldGiveNoPointName(string url)
    {
        // act
        var target = MapLinkParser.TryParse(url);

        // assert
        (target?.Name ?? "").Should().BeEmpty();
    }
}

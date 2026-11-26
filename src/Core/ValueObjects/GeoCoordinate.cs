namespace DispatchGuard.Core.ValueObjects;

public sealed class GeoCoordinate : IEquatable<GeoCoordinate>
{
    public decimal Latitude { get; }
    public decimal Longitude { get; }

    public GeoCoordinate(decimal latitude, decimal longitude)
    {
        if (latitude < -90 || latitude > 90)
        {
            throw new InvalidRequestException(Texts.FieldBetween("Latitude", -90, 90));
        }

        if (longitude < -180 || longitude > 180)
        {
            throw new InvalidRequestException(Texts.FieldBetween("Longitude", -180, 180));
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Haversine formula, in kilometers.</summary>
    public double DistanceTo(GeoCoordinate other)
    {
        const double earthRadiusKm = 6371;

        var dLat = DegreesToRadians((double)(other.Latitude - Latitude));
        var dLon = DegreesToRadians((double)(other.Longitude - Longitude));

        var lat1 = DegreesToRadians((double)Latitude);
        var lat2 = DegreesToRadians((double)other.Latitude);

        var haversine = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);

        var centralAngle = 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));

        return earthRadiusKm * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    public bool Equals(GeoCoordinate? other)
    {
        if (other is null)
        {
            return false;
        }

        return Latitude == other.Latitude && Longitude == other.Longitude;
    }

    public override bool Equals(object? obj) => Equals(obj as GeoCoordinate);

    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude);

    public override string ToString() => $"{Latitude:F6}, {Longitude:F6}";
}
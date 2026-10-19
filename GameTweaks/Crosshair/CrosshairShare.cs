using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GameTweaks.Core;

namespace GameTweaks.Crosshair;

/// <summary>Short text codes for sharing crosshairs with friends ("SPUD1:..."), like crosshair codes in other games.</summary>
public static class CrosshairShare
{
    const string Prefix = "SPUD1:";

    /// <summary>Everything that defines the crosshairs' look and keys; not personal on/off preferences.</summary>
    sealed class Payload
    {
        public CrosshairLook Default { get; set; } = new();
        public List<WeaponCrosshair> Weapons { get; set; } = [];
        public string DefaultKeys { get; set; } = "";
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double FireSpread { get; set; }
        public bool HideWhileAiming { get; set; }
    }

    public static string Export(CrosshairSettings s)
    {
        var payload = new Payload
        {
            Default = Copy(s, new CrosshairLook()),
            Weapons = s.Weapons,
            DefaultKeys = s.DefaultKeys,
            OffsetX = s.OffsetX,
            OffsetY = s.OffsetY,
            FireSpread = s.FireSpread,
            HideWhileAiming = s.HideWhileAiming,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize)) deflate.Write(json);
        return Prefix + Convert.ToBase64String(buffer.ToArray());
    }

    /// <summary>Applies a share code to <paramref name="s"/>. Throws FormatException for anything that isn't a valid code.</summary>
    public static void Import(string code, CrosshairSettings s)
    {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("That isn't a SpudBoost crosshair code (they start with SPUD1:).");
        Payload? payload;
        try
        {
            using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(code[Prefix.Length..])), CompressionMode.Decompress);
            payload = JsonSerializer.Deserialize<Payload>(input);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            throw new FormatException("That crosshair code is damaged or incomplete. Copy it again.", ex);
        }
        if (payload == null) throw new FormatException("That crosshair code is empty.");

        Copy(payload.Default, s);
        s.Weapons = payload.Weapons;
        s.DefaultKeys = payload.DefaultKeys;
        s.OffsetX = payload.OffsetX;
        s.OffsetY = payload.OffsetY;
        s.FireSpread = payload.FireSpread;
        s.HideWhileAiming = payload.HideWhileAiming;
    }

    static T Copy<T>(CrosshairLook from, T to) where T : CrosshairLook
    {
        to.Style = from.Style;
        to.Color = from.Color;
        to.Size = from.Size;
        to.Thickness = from.Thickness;
        to.Gap = from.Gap;
        to.Opacity = from.Opacity;
        to.Outline = from.Outline;
        to.CenterDot = from.CenterDot;
        return to;
    }
}

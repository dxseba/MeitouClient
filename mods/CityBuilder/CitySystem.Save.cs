using Meitou.Modding;

namespace Meitou.CityBuilder;

/// <summary>
/// The saved state, in the mod's sidecar (the original keeps its own <c>saves/&lt;save&gt;.txt</c> beside its DLL, <c>KENSHI_CITY|8</c>): the roll
/// counter and every building by site id with its state, arrival, price, shop, template, last paid day, income and furnished flag. The people
/// are squads of the player's faction, saved by the game with their tags.
/// </summary>
internal sealed partial class CitySystem : IModSaveData
{
    const int Version = 2;

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Version);
        w.Write(_rolls);
        w.Write(_places.Count);
        foreach (var p in _places.Values)
        {
            w.Write(p.Site);
            w.Write(p.Record);
            w.Write((int)p.State);
            w.Write(p.ArriveAt);
            w.Write(p.Price);
            w.Write(p.Shop);
            w.Write(p.Template);
            w.Write(p.LastPaidDay);
            w.Write(p.Income);
            w.Write(p.LastGuests);
            w.Write(p.Furnished);
            w.Write(p.Pieces.Count);
            foreach (string piece in p.Pieces) w.Write(piece);
            w.Write(p.PendingGuards);
            w.Write(p.GuestDay);
        }

        WriteTidy(w);
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = r.ReadInt32();
        if (version is < 1 or > Version) throw new InvalidDataException("Unknown City save version");

        _rolls = r.ReadUInt64();
        _places.Clear();
        for (int count = r.ReadInt32(), i = 0; i < count; i++)
        {
            var p = new CityPlace { Site = r.ReadString(), Record = r.ReadString() };
            p.State = (PlaceState)r.ReadInt32();
            p.ArriveAt = r.ReadDouble();
            p.Price = r.ReadInt64();
            p.Shop = r.ReadInt32();
            p.Template = r.ReadString();
            p.LastPaidDay = r.ReadInt32();
            p.Income = r.ReadInt64();
            p.LastGuests = r.ReadInt32();
            p.Furnished = r.ReadBoolean();
            if (version >= 2) ReadV2(r, p);
            _places[p.Site] = p;
        }

        if (version >= 2) ReadTidy(r);
    }

    static void ReadV2(BinaryReader r, CityPlace p)
    {
        for (int pieces = r.ReadInt32(), k = 0; k < pieces; k++) p.Pieces.Add(r.ReadString());
        p.PendingGuards = r.ReadInt32();
        p.GuestDay = r.ReadInt32();
    }
}

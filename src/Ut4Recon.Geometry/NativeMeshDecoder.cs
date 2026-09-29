using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class NativeMeshDecoder
{
    public StaticMeshIr Decode(string providerRoot, string virtualPath)
    {
        providerRoot = Path.GetFullPath(providerRoot);
        var physicalPath = Path.GetFullPath(Path.Combine(providerRoot, virtualPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!physicalPath.StartsWith(providerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Virtual package path escapes provider root.");
        if (!File.Exists(physicalPath)) throw new FileNotFoundException("Cooked mesh package was not found.", physicalPath);

        var diagnostics = new List<string>();
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(providerRoot, SearchOption.AllDirectories, true, new VersionContainer(EGame.GAME_UE4_15));
#pragma warning restore CS0618
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));
        var normalizedVirtualPath = virtualPath.Replace('\\', '/');
        var candidates = provider.Files.Where(x => x.Key.EndsWith('/' + normalizedVirtualPath, StringComparison.OrdinalIgnoreCase) || x.Key.Equals(Path.GetFileName(normalizedVirtualPath), StringComparison.OrdinalIgnoreCase)).ToArray();
        var exact = provider.Files.Where(x => x.Key.Equals(normalizedVirtualPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = exact.Length == 1 ? exact : candidates;
        if (selected.Length != 1) throw new FileNotFoundException($"Cooked mesh selection resolved to {selected.Length} indexed files. Nearby keys: {string.Join(", ", provider.Files.Keys.Where(x => x.Contains(Path.GetFileNameWithoutExtension(normalizedVirtualPath), StringComparison.OrdinalIgnoreCase)).Take(10))}", normalizedVirtualPath);
        var file = selected[0].Value;
        var exports = provider.LoadPackage(file).GetExports().ToArray();
        var meshes = exports.OfType<UStaticMesh>().ToArray();
        if (meshes.Length != 1) throw new InvalidDataException($"Expected one StaticMesh export, found {meshes.Length}.");
        var mesh = meshes[0];
        if (mesh.RenderData?.LODs is null) throw new InvalidDataException("StaticMesh render data is absent.");

        var lods = new List<MeshLod>();
        for (var lodIndex = 0; lodIndex < mesh.RenderData.LODs.Length; lodIndex++)
        {
            var lod = mesh.RenderData.LODs[lodIndex];
            var positions = Enumerate(Member(lod.PositionVertexBuffer!, "Verts")).Select(Vector).ToArray();
            var indices = (lod.IndexBuffer!.Indices16?.Select(x => (uint)x).ToArray() ?? lod.IndexBuffer.Indices32) ?? [];
            var tangentX = new List<MeshVector>(); var tangentZ = new List<MeshVector>(); var uvChannels = new List<List<MeshUv>>();
            foreach (var vertex in Enumerate(Member(lod.VertexBuffer!, "UV")))
            {
                var normals = Enumerate(Member(vertex, "Normal")).ToArray();
                tangentX.Add(normals.Length > 0 ? PackedNormal(normals[0]) : new MeshVector(0, 0, 0));
                tangentZ.Add(normals.Length > 2 ? PackedNormal(normals[2]) : normals.Length > 1 ? PackedNormal(normals[1]) : new MeshVector(0, 0, 0));
                var uvs = Enumerate(Member(vertex, "UV")).Select(Uv).ToArray();
                while (uvChannels.Count < uvs.Length) uvChannels.Add([]);
                for (var channel = 0; channel < uvs.Length; channel++) uvChannels[channel].Add(uvs[channel]);
            }
            var sections = Enumerate(Member(lod, "Sections")).Select((section, index) => new MeshSection(
                Int(section, "MaterialIndex"), Int(section, "FirstIndex"), Int(section, "NumTriangles"), Int(section, "MinVertexIndex"), Int(section, "MaxVertexIndex"),
                Bool(section, "bEnableCollision", "EnableCollision"), Bool(section, "bCastShadow", "CastShadow"))).ToArray();
            if (positions.Length == 0 || indices.Length == 0) diagnostics.Add($"LOD {lodIndex} has an empty render stream.");
            if (indices.Any(x => x >= positions.Length)) diagnostics.Add($"LOD {lodIndex} contains an out-of-range vertex index.");
            lods.Add(new MeshLod(lodIndex, positions, indices, tangentX, tangentZ, uvChannels.Select(x => (IReadOnlyList<MeshUv>)x).ToArray(), sections,
                HashVectors(positions), HashIndices(indices), HashAttributes(tangentX, tangentZ, uvChannels)));
        }

        var bounds = mesh.RenderData.Bounds!;
        var meshBounds = new MeshBounds(Vector(Member(bounds, "Origin")), Vector(Member(bounds, "BoxExtent")), Number(bounds, "SphereRadius"));
        var asset = new UAsset(physicalPath, EngineVersion.VER_UE4_15);
        var meshName = mesh.Name.ToString();
        var staticMeshIndex = asset.Exports.FindIndex(x => x.ObjectName.ToString() == meshName) + 1;
        var collision = DecodeCollision(asset, physicalPath, exports, diagnostics);
        return new StaticMeshIr(GeometryFormatVersions.StaticMeshIr, GeometryFormatVersions.Profile, virtualPath.Replace('\\', '/'), Identity.Sha256File(physicalPath), meshName, staticMeshIndex, meshBounds, lods, collision, diagnostics);
    }

    private static CollisionIr? DecodeCollision(UAsset asset, string physicalPath, object[] cueExports, List<string> diagnostics)
    {
        var matches = asset.Exports.Select((x, i) => (Export: x, Index: i + 1)).Where(x => x.Export.GetExportClassType().ToString().Contains("BodySetup", StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) { diagnostics.Add($"Expected one BodySetup export, found {matches.Length}."); return null; }
        var body = matches[0];
        using var packageJson = JsonDocument.Parse(asset.SerializeJson());
        var bodyJson = packageJson.RootElement.GetProperty("Exports")[body.Index - 1];
        var hulls = ReadConvexHulls(bodyJson, diagnostics);
        var payloads = new List<CookedCollisionPayload>();
        var cueBody = cueExports.SingleOrDefault(x => String(Member(x, "Name")) == body.Export.ObjectName.ToString());
        if (cueBody is not null)
        {
            using var cueJson = JsonDocument.Parse(JsonConvert.SerializeObject(cueBody));
            if (cueJson.RootElement.TryGetProperty("CookedFormatData", out var formats))
            {
                foreach (var format in formats.EnumerateObject())
                {
                    var value = format.Value; var count = value.GetProperty("ElementCount").GetInt32(); var size = value.GetProperty("SizeOnDisk").GetInt32();
                    var offsetText = value.GetProperty("OffsetInFile").ToString(); var offset = offsetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt64(offsetText[2..], 16) : long.Parse(offsetText, CultureInfo.InvariantCulture);
                    var flags = value.GetProperty("BulkDataFlags").GetString() ?? "";
                    var inline = flags.Contains("ForceInlinePayload", StringComparison.Ordinal);
                    string payloadHash;
                    if (inline && size <= body.Export.Extras.Length) payloadHash = Identity.Sha256Bytes(body.Export.Extras.AsSpan(body.Export.Extras.Length - size));
                    else payloadHash = HashRange(physicalPath, offset, size);
                    payloads.Add(new CookedCollisionPayload(format.Name, count, size, offset, payloadHash, inline));
                }
            }
        }
        if (payloads.Count == 0) diagnostics.Add("BodySetup has no decoded cooked collision payload metadata.");
        var taggedHash = Identity.Sha256Bytes(System.Text.Encoding.UTF8.GetBytes(bodyJson.GetProperty("Data").GetRawText()));
        var query = ConvexCollisionQueries.Fingerprint(hulls);
        return new CollisionIr(body.Index, body.Export.ObjectName.ToString(), hulls, payloads, taggedHash, Identity.Sha256Bytes(body.Export.Extras), query.Count, query.Fingerprint, hulls.Count > 0 && payloads.Any(x => x.Format == "PhysXPC" && x.SizeOnDisk > 0));
    }

    private static IReadOnlyList<ConvexHull> ReadConvexHulls(JsonElement body, List<string> diagnostics)
    {
        var result = new List<ConvexHull>();
        try
        {
            var agg = body.GetProperty("Data").EnumerateArray().Single(x => x.GetProperty("Name").GetString() == "AggGeom");
            var convex = agg.GetProperty("Value").EnumerateArray().Single(x => x.GetProperty("Name").GetString() == "ConvexElems");
            var index = 0;
            foreach (var item in convex.GetProperty("Value").EnumerateArray())
            {
                var verticesProperty = item.GetProperty("Value").EnumerateArray().Single(x => x.GetProperty("Name").GetString() == "VertexData");
                var vertices = verticesProperty.GetProperty("Value").EnumerateArray().Select(x => Vector(x.GetProperty("Value")[0].GetProperty("Value"))).ToArray();
                var min = new MeshVector(vertices.Min(x => x.X), vertices.Min(x => x.Y), vertices.Min(x => x.Z));
                var max = new MeshVector(vertices.Max(x => x.X), vertices.Max(x => x.Y), vertices.Max(x => x.Z));
                result.Add(new ConvexHull(index++, vertices, min, max, HashVectors(vertices)));
            }
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
        {
            diagnostics.Add("BodySetup AggGeom is outside the convex-hull decoder profile: " + error.Message);
        }
        return result;
    }

    private static string HashRange(string path, long offset, int size)
    {
        using var stream = File.OpenRead(path); if (offset < 0 || size < 0 || offset + size > stream.Length) throw new InvalidDataException("Cooked collision bulk range lies outside the package.");
        stream.Position = offset; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; var left = size;
        while (left > 0) { var read = stream.Read(buffer, 0, Math.Min(buffer.Length, left)); if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); left -= read; }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string HashVectors(IEnumerable<MeshVector> values) => Hash(writer => { foreach (var v in values) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); } });
    private static string HashIndices(IEnumerable<uint> values) => Hash(writer => { foreach (var v in values) writer.Write(v); });
    private static string HashAttributes(IEnumerable<MeshVector> x, IEnumerable<MeshVector> z, IEnumerable<List<MeshUv>> uv) => Hash(writer =>
    {
        foreach (var v in x.Concat(z)) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
        foreach (var channel in uv) foreach (var v in channel) { writer.Write(v.U); writer.Write(v.V); }
    });
    private static string Hash(Action<BinaryWriter> write) { using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) write(writer); return Identity.Sha256Bytes(memory.ToArray()); }

    private static IEnumerable<object> Enumerate(object? value) => value is IEnumerable enumerable ? enumerable.Cast<object>() : [];
    private static object? Member(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value) ?? value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    private static object MemberRequired(object value, string name) => Member(value, name) ?? throw new InvalidDataException($"Native field is absent: {value.GetType().Name}.{name}");
    private static string String(object? value) => value?.ToString() ?? "";
    private static int Int(object value, string name) => Convert.ToInt32(MemberRequired(value, name), CultureInfo.InvariantCulture);
    private static double Number(object value, string name) => Convert.ToDouble(MemberRequired(value, name), CultureInfo.InvariantCulture);
    private static bool Bool(object value, params string[] names) { foreach (var name in names) { var found = Member(value, name); if (found is not null) return Convert.ToBoolean(found, CultureInfo.InvariantCulture); } return false; }
    private static MeshVector Vector(object? value)
    {
        if (value is JsonElement json) return new MeshVector(json.GetProperty("X").GetDouble(), json.GetProperty("Y").GetDouble(), json.GetProperty("Z").GetDouble());
        if (value is null) throw new InvalidDataException("Vector is null."); return new MeshVector(Number(value, "X"), Number(value, "Y"), Number(value, "Z"));
    }
    private static MeshVector PackedNormal(object value)
    {
        var x = Member(value, "X"); if (x is not null) return new MeshVector(Convert.ToDouble(x, CultureInfo.InvariantCulture), Number(value, "Y"), Number(value, "Z"));
        return new MeshVector(0, 0, 0);
    }
    private static MeshUv Uv(object value) => new(NumberAny(value, "X", "U"), NumberAny(value, "Y", "V"));
    private static double NumberAny(object value, params string[] names)
    {
        foreach (var name in names) { var found = Member(value, name); if (found is not null) return Convert.ToDouble(found, CultureInfo.InvariantCulture); }
        throw new InvalidDataException($"Native numeric field is absent: {value.GetType().Name}.{string.Join('/', names)}");
    }
}

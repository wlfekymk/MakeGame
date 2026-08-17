using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Maldives
{
    /// <summary>
    /// maldives_meshes.bytes 리더.
    /// JSON 과 내용은 같지만 파싱이 훨씬 빠릅니다(약 0.5 MB, 리틀엔디언).
    ///
    /// 포맷:
    ///   char[4] "MLDV" | uint32 version | uint32 shapeCount
    ///   shape:
    ///     uint32 idLen | byte[idLen] id(UTF8)
    ///     byte kind (0=island, 1=zone)
    ///     float32 originX | float32 originZ
    ///     uint32 ringCount   | uint32[ringCount] ringStarts
    ///     uint32 vertCount   | float32[vertCount*2] xz (로컬 미터)
    ///     uint32 indexCount  | uint32[indexCount] tris
    /// </summary>
    public static class MaldivesMeshPack
    {
        public static List<MaldivesShape> Read(TextAsset asset)
        {
            return asset == null ? new List<MaldivesShape>() : Read(asset.bytes);
        }

        public static List<MaldivesShape> Read(byte[] data)
        {
            var result = new List<MaldivesShape>();
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                var magic = new string(br.ReadChars(4));
                if (magic != "MLDV")
                {
                    Debug.LogError("MaldivesMeshPack: 잘못된 파일 헤더 '" + magic + "'");
                    return result;
                }
                br.ReadUInt32();                       // version
                int count = (int)br.ReadUInt32();
                for (int s = 0; s < count; s++)
                {
                    var shape = new MaldivesShape();
                    int idLen = (int)br.ReadUInt32();
                    shape.id = System.Text.Encoding.UTF8.GetString(br.ReadBytes(idLen));
                    shape.kind = br.ReadByte() == 1 ? "zone" : "island";
                    shape.originX = br.ReadSingle();
                    shape.originZ = br.ReadSingle();

                    int ringCount = (int)br.ReadUInt32();
                    shape.ringStarts = new int[ringCount];
                    for (int i = 0; i < ringCount; i++) shape.ringStarts[i] = (int)br.ReadUInt32();

                    int vertCount = (int)br.ReadUInt32();
                    shape.verts = new float[vertCount * 2];
                    for (int i = 0; i < shape.verts.Length; i++) shape.verts[i] = br.ReadSingle();

                    int idxCount = (int)br.ReadUInt32();
                    shape.tris = new int[idxCount];
                    for (int i = 0; i < idxCount; i++) shape.tris[i] = (int)br.ReadUInt32();

                    var ll = MaldivesGeo.PlaneToLatLon(shape.originX, shape.originZ);
                    shape.lat = ll.x;
                    shape.lon = ll.y;
                    shape.zone = shape.kind == "zone" ? shape.id : shape.id.Split('-')[0];
                    result.Add(shape);
                }
            }
            return result;
        }
    }
}

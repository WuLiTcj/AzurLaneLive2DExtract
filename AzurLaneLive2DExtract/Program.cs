﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using AssetStudioCore;
using AssetStudioCore.Classes;
using Newtonsoft.Json;

namespace AzurLaneLive2DExtract
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach (var arg in args)
            {
                if (!File.Exists(arg))
                    continue;
                var path = Path.GetFullPath(arg);
                var bundleFile = new BundleFile(path, new EndianBinaryReader(File.OpenRead(path)));
                if (bundleFile.fileList.Count == 0)
                {
                    return;
                }
                var assetsFile = new AssetsFile(path, new EndianBinaryReader(bundleFile.fileList[0].stream));
                var assets = assetsFile.preloadTable.Select(x => x.Value).ToArray();
                var name = Path.GetFileName(path);
                var destPath = @"live2d\" + name + @"\";
                var destTexturePath = @"live2d\" + name + @"\textures\";
                var destAnimationPath = @"live2d\" + name + @"\motions\";
                Directory.CreateDirectory(destPath);
                Directory.CreateDirectory(destTexturePath);
                Directory.CreateDirectory(destAnimationPath);
                Console.WriteLine($"Extract {name}");
                //physics
                var physics = new TextAsset(assets.First(x => x.Type == ClassIDReference.TextAsset));
                File.WriteAllBytes($"{destPath}{physics.m_Name}.json", physics.m_Script);
                //moc
                var moc = assets.First(x => x.Type == ClassIDReference.MonoBehaviour);
                foreach (var assetPreloadData in assets.Where(x => x.Type == ClassIDReference.MonoBehaviour))
                {
                    if (assetPreloadData.Size > moc.Size)
                    {
                        moc = assetPreloadData;
                    }
                }
                var mocReader = moc.InitReader();
                mocReader.Position += 28;
                mocReader.ReadAlignedString();
                var mocBuff = mocReader.ReadBytes(mocReader.ReadInt32());
                File.WriteAllBytes($"{destPath}{name}.moc3", mocBuff);
                //texture
                var textures = new SortedSet<string>();
                foreach (var texture in assets.Where(x => x.Type == ClassIDReference.Texture2D))
                {
                    var texture2D = new Texture2D(texture);
                    using (var bitmap = new Texture2DConverter(texture2D).ConvertToBitmap(true))
                    {
                        textures.Add($"textures/{texture2D.m_Name}.png");
                        bitmap.Save($"{destTexturePath}{texture2D.m_Name}.png", ImageFormat.Png);
                    }
                }
                //motions
                var motions = new List<string>();
                var animatorAsset = assets.First(x => x.Type == ClassIDReference.Animator);
                var animator = new Animator(animatorAsset);
                var rootGameObject = new GameObject(animator.m_GameObject.Get());
                var animations = assets.Where(x => x.Type == ClassIDReference.AnimationClip).Select(x => new AnimationClip(x)).ToArray();
                var converter = new CubismMotion3Converter(rootGameObject, animations);
                foreach (ImportedKeyframedAnimation animation in converter.AnimationList)
                {
                    var json = new CubismMotion3Json
                    {
                        Version = 3,
                        Meta = new SerializableMeta
                        {
                            Duration = animation.Duration,
                            Fps = animation.SampleRate,
                            Loop = true,
                            AreBeziersRestricted = true,
                            CurveCount = animation.TrackList.Count,
                            TotalSegmentCount = 63, //TODO How to calculate this?
                            TotalPointCount = 165, //TODO How to calculate this?
                            UserDataCount = 0,
                            TotalUserDataSize = 0
                        },
                        Curves = new SerializableCurve[animation.TrackList.Count]
                    };
                    for (int i = 0; i < animation.TrackList.Count; i++)
                    {
                        var track = animation.TrackList[i];
                        json.Curves[i] = new SerializableCurve
                        {
                            Target = track.Target,
                            Id = track.Name,
                            Segments = new List<float> { 0f, track.Curve[0].value }
                        };
                        for (var j = 1; j < track.Curve.Count; j++)
                        {
                            var curve = track.Curve[j];
                            if (curve != null)
                            {
                                json.Curves[i].Segments.Add(0f);
                                json.Curves[i].Segments.Add(curve.time);
                                json.Curves[i].Segments.Add(curve.value);
                            }
                        }
                    }
                    motions.Add($"motions/{animation.Name}.motion3.json");
                    File.WriteAllText($"{destAnimationPath}{animation.Name}.motion3.json", JsonConvert.SerializeObject(json, Formatting.Indented, new MyJsonConverter()));
                }
                //model
                var model3 = new CubismModel3Json
                {
                    Version = 3,
                    FileReferences = new SerializableFileReferences
                    {
                        Moc = $"{name}.moc3",
                        Textures = textures.ToArray(),
                        Motions = motions.ToArray(),
                        Physics = $"{physics.m_Name}.json"
                    },
                    Groups = new[]
                    {
                        new SerializableGroup
                        {
                            Target = "Parameter",
                            Name = "LipSync",
                            Ids = new[] {"ParamMouthOpenY"}
                        },
                        new SerializableGroup
                        {
                            Target = "Parameter",
                            Name = "EyeBlink",
                            Ids = new[] {"ParamEyeLOpen", "ParamEyeROpen"}
                        }
                    }
                };
                File.WriteAllText($"{destPath}{name}.model3.json", JsonConvert.SerializeObject(model3, Formatting.Indented));
            }
            Console.WriteLine("Done!");
            Console.Read();
        }
    }
}

﻿#region ================== Namespaces

using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Collections.Generic;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.GZBuilder.Data;
using SlimDX;
using SlimDX.Direct3D9;
using CodeImp.DoomBuilder.Geometry;

#endregion

//mxd. Original version taken from here: http://colladadotnet.codeplex.com/SourceControl/changeset/view/40680
namespace CodeImp.DoomBuilder.GZBuilder.MD3
{
	internal static class ModelReader
	{
		#region ================== Variables

		internal class MD3LoadResult
		{
			public List<string> Skins;
			public List<Mesh> Meshes;
			public string Errors;

			public MD3LoadResult() 
			{
				Skins = new List<string>();
				Meshes = new List<Mesh>();
			}
		}

		private static readonly VertexElement[] vertexElements = new[] 
		{
			new VertexElement(0, 0, DeclarationType.Float3, DeclarationMethod.Default, DeclarationUsage.Position, 0),
			new VertexElement(0, 12, DeclarationType.Color, DeclarationMethod.Default, DeclarationUsage.Color, 0),
			new VertexElement(0, 16, DeclarationType.Float2, DeclarationMethod.Default, DeclarationUsage.TextureCoordinate, 0),
			new VertexElement(0, 24, DeclarationType.Float3, DeclarationMethod.Default, DeclarationUsage.Normal, 0),
			VertexElement.VertexDeclarationEnd
		};

		#endregion

		#region ================== Load

		public static void Load(ModelData mde, List<DataReader> containers, Device device) 
		{
			if(mde.IsVoxel) LoadKVX(mde, containers, device);
			else LoadModel(mde, containers, device);
		}

		private static void LoadKVX(ModelData mde, List<DataReader> containers, Device device) 
		{
			mde.Model = new GZModel();
			string unused = string.Empty;
			foreach(string name in mde.ModelNames)
			{
				//find the model
				foreach(DataReader dr in containers) 
				{
					Stream ms = dr.GetVoxelData(name, ref unused);
					if(ms == null) continue;

					//load kvx
					ReadKVX(mde, ms, device);

					//done
					ms.Close();
					break;
				}
			}

			//clear unneeded data
			mde.TextureNames = null;
			mde.ModelNames = null;

			if(mde.Model.Meshes == null || mde.Model.Meshes.Count == 0) 
			{
				mde.Model = null;
			}
		}

		private static void LoadModel(ModelData mde, List<DataReader> containers, Device device) 
		{
			mde.Model = new GZModel();
			BoundingBoxSizes bbs = new BoundingBoxSizes();
			MD3LoadResult result = new MD3LoadResult();

			//load models and textures
			for(int i = 0; i < mde.ModelNames.Count; i++) 
			{
				//need to use model skins?
				bool useSkins = string.IsNullOrEmpty(mde.TextureNames[i]);
			
				//load mesh
				MemoryStream ms = LoadFile(containers, mde.ModelNames[i], true);
				if(ms == null) 
				{
					General.ErrorLogger.Add(ErrorType.Error, "Error while loading \"" + mde.ModelNames[i] + "\": unable to find file.");
					continue;
				}

				string ext = Path.GetExtension(mde.ModelNames[i]);
				switch(ext)
				{
					case ".md3":
						if(!string.IsNullOrEmpty(mde.FrameNames[i]))
						{
							General.ErrorLogger.Add(ErrorType.Error, "Error while loading \"" + mde.ModelNames[i] + "\": frame names are not supported for MD3 models!");
							continue;
						}
						result = ReadMD3Model(ref bbs, useSkins, ms, device, mde.FrameIndices[i]);
						break;
					case ".md2":
						result = ReadMD2Model(ref bbs, ms, device, mde.FrameIndices[i], mde.FrameNames[i]);
						break;
					default:
						result.Errors = "model format is not supported";
						break;
				}

				ms.Close();

				//got errors?
				if(!String.IsNullOrEmpty(result.Errors)) 
				{
					General.ErrorLogger.Add(ErrorType.Error, "Error while loading \"" + mde.ModelNames[i] + "\": " + result.Errors);
				} 
				else 
				{
					//add loaded data to ModeldefEntry
					mde.Model.Meshes.AddRange(result.Meshes);

					//prepare UnknownTexture3D... just in case :)
					if(General.Map.Data.UnknownTexture3D.Texture == null || General.Map.Data.UnknownTexture3D.Texture.Disposed)
						General.Map.Data.UnknownTexture3D.CreateTexture();

					//load texture
					List<string> errors = new List<string>();

					// Texture not defined in MODELDEF?
					if(useSkins) 
					{
						//try to use model's own skins 
						for(int m = 0; m < result.Meshes.Count; m++) 
						{
							if(string.IsNullOrEmpty(result.Skins[m])) 
							{
								mde.Model.Textures.Add(General.Map.Data.UnknownTexture3D.Texture);
								errors.Add("texture not found in MODELDEF or model skin.");
								continue;
							}

							string path = result.Skins[m].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
							ext = Path.GetExtension(path);

							if(Array.IndexOf(ModelData.SUPPORTED_TEXTURE_EXTENSIONS, ext) == -1) 
							{
								mde.Model.Textures.Add(General.Map.Data.UnknownTexture3D.Texture);
								errors.Add("image format \"" + ext + "\" is not supported!");
								continue;
							}

							//relative path?
							if(path.IndexOf(Path.DirectorySeparatorChar) == -1)
								path = Path.Combine(Path.GetDirectoryName(mde.ModelNames[useSkins ? i : m]), path);

							Texture t = LoadTexture(containers, path, device);

							if(t == null) 
							{
								mde.Model.Textures.Add(General.Map.Data.UnknownTexture3D.Texture);
								errors.Add("unable to load skin \"" + result.Skins[m] + "\"");
								continue;
							} 

							mde.Model.Textures.Add(t);
						}
					}
					//Try to use texture loaded from MODELDEFS
					else
					{
						Texture t = LoadTexture(containers, mde.TextureNames[i], device);
						if(t == null) 
						{
							mde.Model.Textures.Add(General.Map.Data.UnknownTexture3D.Texture);
							errors.Add("unable to load texture \"" + mde.TextureNames[i] + "\"");
						} 
						else 
						{
							mde.Model.Textures.Add(t);
						}
					}

					//report errors
					if(errors.Count > 0) 
					{
						foreach(string e in errors)
							General.ErrorLogger.Add(ErrorType.Error, "Error while loading \"" + mde.ModelNames[i] + "\": " + e);
					}
				}
			}

			//clear unneeded data
			mde.TextureNames = null;
			mde.ModelNames = null;

			if(mde.Model.Meshes == null || mde.Model.Meshes.Count == 0) 
			{
				mde.Model = null;
				return;
			}

			//scale bbs
			bbs.MaxX = (int)(bbs.MaxX * mde.Scale.X);
			bbs.MinX = (int)(bbs.MinX * mde.Scale.X);
			bbs.MaxY = (int)(bbs.MaxY * mde.Scale.Y);
			bbs.MinY = (int)(bbs.MinY * mde.Scale.Y);

			//calculate model radius
			mde.Model.Radius = Math.Max(Math.Max(Math.Abs(bbs.MinY), Math.Abs(bbs.MaxY)), Math.Max(Math.Abs(bbs.MinX), Math.Abs(bbs.MaxX))); 
		}

		#endregion

		#region ================== MD3

		internal static MD3LoadResult ReadMD3Model(ref BoundingBoxSizes bbs, bool useSkins, Stream s, Device device, int frame) 
		{
			long start = s.Position;
			MD3LoadResult result = new MD3LoadResult();

			using(var br = new BinaryReader(s, Encoding.ASCII)) 
			{
				string magic = ReadString(br, 4);
				if(magic != "IDP3")
				{
					result.Errors = "unknown header: expected \"IDP3\", but got \"" + magic + "\"";
					return result;
				}

				int modelVersion = br.ReadInt32();
				if(modelVersion != 15) //MD3 version. Must be equal to 15
				{
					result.Errors = "expected MD3 version 15, but got " + modelVersion;
					return result;
				}

				s.Position += 76;
				int numSurfaces = br.ReadInt32();
				s.Position += 12;
				int ofsSurfaces = br.ReadInt32();

				s.Position = ofsSurfaces + start;

				List<int> polyIndecesList = new List<int>();
				List<WorldVertex> vertList = new List<WorldVertex>();

				Dictionary<string, List<List<int>>> polyIndecesListsPerTexture = new Dictionary<string, List<List<int>>>(StringComparer.Ordinal);
				Dictionary<string, List<WorldVertex>> vertListsPerTexture = new Dictionary<string, List<WorldVertex>>(StringComparer.Ordinal);
				Dictionary<string, List<int>> vertexOffsets = new Dictionary<string, List<int>>(StringComparer.Ordinal);

				for(int c = 0; c < numSurfaces; c++) 
				{
					string skin = "";
					string error = ReadSurface(ref bbs, ref skin, br, polyIndecesList, vertList, frame);

					if(!string.IsNullOrEmpty(error)) 
					{
						result.Errors = error;
						return result;
					}

					if(useSkins) 
					{
						if(polyIndecesListsPerTexture.ContainsKey(skin)) 
						{
							polyIndecesListsPerTexture[skin].Add(polyIndecesList);
							vertListsPerTexture[skin].AddRange(vertList.ToArray());
							vertexOffsets[skin].Add(vertList.Count);
						} 
						else 
						{
							polyIndecesListsPerTexture.Add(skin, new List<List<int>> { polyIndecesList } );
							vertListsPerTexture.Add(skin, vertList);
							vertexOffsets.Add(skin, new List<int> { vertList.Count });
						}

						//reset lists
						polyIndecesList = new List<int>();
						vertList = new List<WorldVertex>();
					}
				}

				if(!useSkins) 
				{ 
					//create mesh
					CreateMesh(device, ref result, vertList, polyIndecesList);
					result.Skins.Add("");
				} 
				else 
				{
					//create a mesh for each surface texture
					foreach(KeyValuePair<string, List<List<int>>> group in polyIndecesListsPerTexture) 
					{
						polyIndecesList = new List<int>();
						int offset = 0;
						
						//collect indices, fix vertex offsets
						for(int i = 0; i < group.Value.Count; i++) 
						{
							if(i > 0) 
							{
								//TODO: Damn I need to rewrite all of this stuff from scratch...
								offset += vertexOffsets[group.Key][i - 1]; 
								for(int c = 0; c < group.Value[i].Count; c++)
									group.Value[i][c] += offset;
							}
							polyIndecesList.AddRange(group.Value[i].ToArray());
						}

						CreateMesh(device, ref result, vertListsPerTexture[group.Key], polyIndecesList);
						result.Skins.Add(group.Key.ToLowerInvariant());
					}
				}
			}

			return result;
		}

		private static string ReadSurface(ref BoundingBoxSizes bbs, ref string skin, BinaryReader br, List<int> polyIndecesList, List<WorldVertex> vertList, int frame) 
		{
			int vertexOffset = vertList.Count;
			long start = br.BaseStream.Position;
			
			string magic = ReadString(br, 4);
			if(magic != "IDP3") return "error while reading surface. Unknown header: expected \"IDP3\", but got \"" + magic + "\"";

			string name = ReadString(br, 64);
			int flags = br.ReadInt32();
			int numFrames = br.ReadInt32(); //Number of animation frames. This should match NUM_FRAMES in the MD3 header.
			int numShaders = br.ReadInt32(); //Number of Shader objects defined in this Surface, with a limit of MD3_MAX_SHADERS. Current value of MD3_MAX_SHADERS is 256.
			int numVerts = br.ReadInt32(); //Number of Vertex objects defined in this Surface, up to MD3_MAX_VERTS. Current value of MD3_MAX_VERTS is 4096.
			int numTriangles = br.ReadInt32(); //Number of Triangle objects defined in this Surface, maximum of MD3_MAX_TRIANGLES. Current value of MD3_MAX_TRIANGLES is 8192.
			int ofsTriangles = br.ReadInt32(); //Relative offset from SURFACE_START where the list of Triangle objects starts.
			int ofsShaders = br.ReadInt32();
			int ofsST = br.ReadInt32(); //Relative offset from SURFACE_START where the list of ST objects (s-t texture coordinates) starts.
			int ofsNormal = br.ReadInt32(); //Relative offset from SURFACE_START where the list of Vertex objects (X-Y-Z-N vertices) starts.
			int ofsEnd = br.ReadInt32(); //Relative offset from SURFACE_START to where the Surface object ends.

			// Sanity check
			if(frame < 0 || frame >= numFrames)
			{
				return "frame " + frame + " is outside of model's frame range [0.." + (numFrames - 1) + "]";
			}

			// Polygons
			if(start + ofsTriangles != br.BaseStream.Position)
				br.BaseStream.Position = start + ofsTriangles;

			for(int i = 0; i < numTriangles * 3; i++)
				polyIndecesList.Add(vertexOffset + br.ReadInt32());

			// Shaders
			if(start + ofsShaders != br.BaseStream.Position)
				br.BaseStream.Position = start + ofsShaders;

			skin = ReadString(br, 64); //we are interested only in the first one

			// Vertices
			if(start + ofsST != br.BaseStream.Position)
				br.BaseStream.Position = start + ofsST;

			for(int i = 0; i < numVerts; i++) 
			{
				WorldVertex v = new WorldVertex();
				v.c = -1; //white
				v.u = br.ReadSingle();
				v.v = br.ReadSingle();

				vertList.Add(v);
			}

			// Positions and normals
			long vertoffset = start + ofsNormal + numVerts * 8 * frame; // The length of Vertex struct is 8 bytes
			if(br.BaseStream.Position != vertoffset) br.BaseStream.Position = vertoffset;

			for(int i = vertexOffset; i < vertexOffset + numVerts; i++) 
			{
				WorldVertex v = vertList[i];

				//read vertex
				v.y = -(float)br.ReadInt16() / 64;
				v.x = (float)br.ReadInt16() / 64;
				v.z = (float)br.ReadInt16() / 64;

				//bounding box
				BoundingBoxTools.UpdateBoundingBoxSizes(ref bbs, v);

				var lat = br.ReadByte() * (2 * Math.PI) / 255.0;
				var lng = br.ReadByte() * (2 * Math.PI) / 255.0;

				v.nx = (float)(Math.Sin(lng) * Math.Sin(lat));
				v.ny = -(float)(Math.Cos(lng) * Math.Sin(lat));
				v.nz = (float)(Math.Cos(lat));

				vertList[i] = v;
			}

			if(start + ofsEnd != br.BaseStream.Position)
				br.BaseStream.Position = start + ofsEnd;
			return "";
		}

		#endregion

		#region ================== MD2

		private static MD3LoadResult ReadMD2Model(ref BoundingBoxSizes bbs, Stream s, Device device, int frame, string framename) 
		{
			long start = s.Position;
			MD3LoadResult result = new MD3LoadResult();

			using(var br = new BinaryReader(s, Encoding.ASCII)) 
			{
				string magic = ReadString(br, 4);
				if(magic != "IDP2") //magic number: "IDP2"
				{
					result.Errors = "unknown header: expected \"IDP2\", but got \"" + magic + "\"";
					return result;
				}

				int modelVersion = br.ReadInt32();
				if(modelVersion != 8) //MD2 version. Must be equal to 8
				{ 
					result.Errors = "expected MD3 version 15, but got " + modelVersion;
					return result;
				}

				int texWidth = br.ReadInt32();
				int texHeight = br.ReadInt32();
				int framesize = br.ReadInt32(); // Size of one frame in bytes
				s.Position += 4; //Number of textures
				int num_verts = br.ReadInt32(); //Number of vertices
				int num_uv = br.ReadInt32(); //The number of UV coordinates in the model
				int num_tris = br.ReadInt32(); //Number of triangles
				s.Position += 4; //Number of OpenGL commands
				int num_frames = br.ReadInt32(); //Total number of frames

				// Sanity checks
				if(frame < 0 || frame >= num_frames)
				{
					result.Errors = "frame " + frame + " is outside of model's frame range [0.." + (num_frames - 1) + "]";
					return result;
				}

				s.Position += 4; //Offset to skin names (each skin name is an unsigned char[64] and are null terminated)
				int ofs_uv = br.ReadInt32();//Offset to s-t texture coordinates
				int ofs_tris = br.ReadInt32(); //Offset to triangles
				int ofs_animFrame = br.ReadInt32(); //An offset to the first animation frame

				List<int> polyIndecesList = new List<int>();
				List<int> uvIndecesList = new List<int>();
				List<Vector2> uvCoordsList = new List<Vector2>();
				List<WorldVertex> vertList = new List<WorldVertex>();

				// Polygons
				s.Position = ofs_tris + start;

				for(int i = 0; i < num_tris; i++) 
				{
					polyIndecesList.Add(br.ReadUInt16());
					polyIndecesList.Add(br.ReadUInt16());
					polyIndecesList.Add(br.ReadUInt16());

					uvIndecesList.Add(br.ReadUInt16());
					uvIndecesList.Add(br.ReadUInt16());
					uvIndecesList.Add(br.ReadUInt16());
				}

				// UV coords
				s.Position = ofs_uv + start;

				for(int i = 0; i < num_uv; i++) 
					uvCoordsList.Add(new Vector2((float)br.ReadInt16() / texWidth, (float)br.ReadInt16() / texHeight));

				// Frames
				// Find correct frame
				if(!string.IsNullOrEmpty(framename))
				{
					// Skip frames untill frame name matches
					bool framefound = false;
					for(int i = 0; i < num_frames; i++)
					{
						s.Position = ofs_animFrame + start + i * framesize;
						s.Position += 24; // Skip scale and translate
						string curframename = ReadString(br, 16).ToLowerInvariant();

						if(curframename == framename)
						{
							// Step back so scale and translate can be read
							s.Position -= 40;
							framefound = true;
							break;
						}
					}

					// No dice? Bail out!
					if(!framefound)
					{
						result.Errors = "unable to find frame \"" + framename + "\"!";
						return result;
					}
				}
				else
				{
					// If we have frame number, we can go directly to target frame
					s.Position = ofs_animFrame + start + frame * framesize;
				}

				Vector3 scale = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
				Vector3 translate = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

				s.Position += 16; // Skip frame name

				// Prepare to fix rotation angle
				float angleOfsetCos = (float)Math.Cos(-Angle2D.PIHALF);
				float angleOfsetSin = (float)Math.Sin(-Angle2D.PIHALF);

				//verts
				for(int i = 0; i < num_verts; i++) 
				{
					WorldVertex v = new WorldVertex();

					v.x = (br.ReadByte() * scale.X + translate.X);
					v.y = (br.ReadByte() * scale.Y + translate.Y);
					v.z = (br.ReadByte() * scale.Z + translate.Z);

					// Fix rotation angle
					float rx = angleOfsetCos * v.x - angleOfsetSin * v.y;
					float ry = angleOfsetSin * v.x + angleOfsetCos * v.y;
					v.y = ry;
					v.x = rx;

					vertList.Add(v);
					s.Position += 1; //vertex normal
				}

				for(int i = 0; i < polyIndecesList.Count; i++) 
				{
					WorldVertex v = vertList[polyIndecesList[i]];
					
					//bounding box
					BoundingBoxTools.UpdateBoundingBoxSizes(ref bbs, new WorldVertex(v.y, v.x, v.z));

					//uv
					float tu = uvCoordsList[uvIndecesList[i]].X;
					float tv = uvCoordsList[uvIndecesList[i]].Y;

					//uv-coordinates already set?
					if(v.c == -1 && (v.u != tu || v.v != tv)) 
					{ 
						//add a new vertex
						vertList.Add(new WorldVertex(v.x, v.y, v.z, -1, tu, tv));
						polyIndecesList[i] = vertList.Count - 1;
					} 
					else 
					{
						v.u = tu;
						v.v = tv;
						v.c = -1; //set color to white

						//return to proper place
						vertList[polyIndecesList[i]] = v;
					}
				}

				//mesh
				Mesh mesh = new Mesh(device, polyIndecesList.Count / 3, vertList.Count, MeshFlags.Use32Bit | MeshFlags.IndexBufferManaged | MeshFlags.VertexBufferManaged, vertexElements);

				using(DataStream stream = mesh.LockVertexBuffer(LockFlags.None)) 
				{
					stream.WriteRange(vertList.ToArray());
				}
				mesh.UnlockVertexBuffer();

				using(DataStream stream = mesh.LockIndexBuffer(LockFlags.None)) 
				{
					stream.WriteRange(polyIndecesList.ToArray());
				}
				mesh.UnlockIndexBuffer();

				mesh.OptimizeInPlace(MeshOptimizeFlags.AttributeSort);

				//store in result
				result.Meshes.Add(mesh);
				result.Skins.Add(""); //no skin support for MD2
			}

			return result;
		}

		#endregion

		#region ================== KVX

		private static void ReadKVX(ModelData mde, Stream stream, Device device) 
		{
			PixelColor[] palette = new PixelColor[256];
			List<WorldVertex> verts = new List<WorldVertex>();
			int xsize, ysize, zsize;
			Vector3D pivot;

			using(BinaryReader reader = new BinaryReader(stream, Encoding.ASCII)) 
			{
				reader.ReadInt32(); //numbytes, we don't use that
				xsize = reader.ReadInt32();
				ysize = reader.ReadInt32();
				zsize = reader.ReadInt32();

				pivot = new Vector3D();
				pivot.x = reader.ReadInt32() / 256f;
				pivot.y = reader.ReadInt32() / 256f;
				pivot.z = reader.ReadInt32() / 256f;

				//read offsets
				int[] xoffset = new int[xsize + 1]; //why is it xsize + 1, not xsize?..
				short[,] xyoffset = new short[xsize, ysize + 1]; //why is it ysize + 1, not ysize?..

				for(int i = 0; i < xoffset.Length; i++) 
				{
					xoffset[i] = reader.ReadInt32();
				}

				for(int x = 0; x < xsize; x++) 
				{
					for(int y = 0; y < ysize + 1; y++) 
					{
						xyoffset[x, y] = reader.ReadInt16();
					}
				}

				//read slabs
				List<int> offsets = new List<int>(xsize * ysize);
				for(int x = 0; x < xsize; x++) 
				{
					for(int y = 0; y < ysize; y++) 
					{
						offsets.Add(xoffset[x] + xyoffset[x, y] + 28); //for some reason offsets are counted from start of xoffset[]...
					}
				}

				int counter = 0;
				int slabsEnd = (int)(reader.BaseStream.Length - 768);

				//read palette
				if(!mde.OverridePalette) 
				{
					reader.BaseStream.Position = slabsEnd;
					for(int i = 0; i < 256; i++) 
					{
						byte r = (byte)(reader.ReadByte() * 4);
						byte g = (byte)(reader.ReadByte() * 4);
						byte b = (byte)(reader.ReadByte() * 4);
						palette[i] = new PixelColor(255, r, g, b);
					}
				} 
				else 
				{
					for(int i = 0; i < 256; i++ ) 
					{
						palette[i] = General.Map.Data.Palette[i];
					}
				}

				for(int x = 0; x < xsize; x++) 
				{
					for(int y = 0; y < ysize; y++) 
					{
						reader.BaseStream.Position = offsets[counter];
						int next = (counter < offsets.Count - 1 ? offsets[counter + 1] : slabsEnd);

						//read slab
						while(reader.BaseStream.Position < next) 
						{
							int ztop = reader.ReadByte();
							int zleng = reader.ReadByte();
							if(ztop + zleng > zsize) break;
							int flags = reader.ReadByte();

							if(zleng > 0) 
							{
								List<int> colorIndices = new List<int>(zleng);
								for(int i = 0; i < zleng; i++) 
								{
									colorIndices.Add(reader.ReadByte());
								}

								if((flags & 16) != 0) 
								{
									AddFace(verts, new Vector3D(x, y, ztop), new Vector3D(x + 1, y, ztop), new Vector3D(x, y + 1, ztop), new Vector3D(x + 1, y + 1, ztop), pivot, colorIndices[0]);
								}

								int z = ztop;
								int cstart = 0;
								while(z < ztop + zleng) 
								{
									int c = 0;
									while(z + c < ztop + zleng && colorIndices[cstart + c] == colorIndices[cstart]) c++;

									if((flags & 1) != 0) 
									{
										AddFace(verts, new Vector3D(x, y, z), new Vector3D(x, y + 1, z), new Vector3D(x, y, z + c), new Vector3D(x, y + 1, z + c), pivot, colorIndices[cstart]);
									}
									if((flags & 2) != 0) 
									{
										AddFace(verts, new Vector3D(x + 1, y + 1, z), new Vector3D(x + 1, y, z), new Vector3D(x + 1, y + 1, z + c), new Vector3D(x + 1, y, z + c), pivot, colorIndices[cstart]);
									}
									if((flags & 4) != 0) 
									{
										AddFace(verts, new Vector3D(x + 1, y, z), new Vector3D(x, y, z), new Vector3D(x + 1, y, z + c), new Vector3D(x, y, z + c), pivot, colorIndices[cstart]);
									}
									if((flags & 8) != 0) 
									{
										AddFace(verts, new Vector3D(x, y + 1, z), new Vector3D(x + 1, y + 1, z), new Vector3D(x, y + 1, z + c), new Vector3D(x + 1, y + 1, z + c), pivot, colorIndices[cstart]);
									}

									if(c == 0) c++;
									z += c;
									cstart += c;
								}

								if((flags & 32) != 0) 
								{
									z = ztop + zleng - 1;
									AddFace(verts, new Vector3D(x + 1, y, z + 1), new Vector3D(x, y, z + 1), new Vector3D(x + 1, y + 1, z + 1), new Vector3D(x, y + 1, z + 1), pivot, colorIndices[zleng - 1]);
								}
							}
						}

						counter++;
					}
				}
			}

			// get model extents
			int minX = (int)((xsize / 2f - pivot.x) * mde.Scale.X);
			int maxX = (int)((xsize / 2f + pivot.x) * mde.Scale.X);
			int minY = (int)((ysize / 2f - pivot.y) * mde.Scale.Y);
			int maxY = (int)((ysize / 2f + pivot.y) * mde.Scale.Y);
			
			// calculate model radius
			mde.Model.Radius = Math.Max(Math.Max(Math.Abs(minY), Math.Abs(maxY)), Math.Max(Math.Abs(minX), Math.Abs(maxX)));

			//create texture
			MemoryStream memstream = new MemoryStream((4096 * 4) + 4096);
			using(Bitmap bmp = CreateVoxelTexture(palette)) bmp.Save(memstream, ImageFormat.Bmp);
			memstream.Seek(0, SeekOrigin.Begin);

			Texture texture = Texture.FromStream(device, memstream, (int)memstream.Length, 64, 64, 0, Usage.None, Format.Unknown, Pool.Managed, Filter.Point, Filter.Box, 0);
			memstream.Dispose();

			//add texture
			mde.Model.Textures.Add(texture);

			//create mesh
			int[] indices = new int[verts.Count];
			for(int i = 0; i < verts.Count; i++) 
			{
				indices[i] = i;
			}

			Mesh mesh = new Mesh(device, verts.Count / 3, verts.Count, MeshFlags.Use32Bit | MeshFlags.IndexBufferManaged | MeshFlags.VertexBufferManaged, vertexElements);

			DataStream mstream = mesh.VertexBuffer.Lock(0, 0, LockFlags.None);
			mstream.WriteRange(verts.ToArray());
			mesh.VertexBuffer.Unlock();

			mstream = mesh.IndexBuffer.Lock(0, 0, LockFlags.None);
			mstream.WriteRange(indices);
			mesh.IndexBuffer.Unlock();

			mesh.OptimizeInPlace(MeshOptimizeFlags.AttributeSort);

			//add mesh
			mde.Model.Meshes.Add(mesh);
		}

		// Shameless GZDoom rip-off
		private static void AddFace(List<WorldVertex> verts, Vector3D v1, Vector3D v2, Vector3D v3, Vector3D v4, Vector3D pivot, int colorIndex) 
		{
			float pu0 = (colorIndex % 16) / 16f;
			float pu1 = pu0 + 0.0001f;
			float pv0 = (colorIndex / 16) / 16f;
			float pv1 = pv0 + 0.0001f;
			
			WorldVertex wv1 = new WorldVertex();
			wv1.x = v1.x - pivot.x;
			wv1.y = -v1.y + pivot.y;
			wv1.z = -v1.z + pivot.z;
			wv1.c = -1;
			wv1.u = pu0;
			wv1.v = pv0;
			verts.Add(wv1);

			WorldVertex wv2 = new WorldVertex();
			wv2.x = v2.x - pivot.x;
			wv2.y = -v2.y + pivot.y;
			wv2.z = -v2.z + pivot.z;
			wv2.c = -1;
			wv2.u = pu1;
			wv2.v = pv1;
			verts.Add(wv2);

			WorldVertex wv4 = new WorldVertex();
			wv4.x = v4.x - pivot.x;
			wv4.y = -v4.y + pivot.y;
			wv4.z = -v4.z + pivot.z;
			wv4.c = -1;
			wv4.u = pu0;
			wv4.v = pv0;
			verts.Add(wv4);

			WorldVertex wv3 = new WorldVertex();
			wv3.x = v3.x - pivot.x;
			wv3.y = -v3.y + pivot.y;
			wv3.z = -v3.z + pivot.z;
			wv3.c = -1;
			wv3.u = pu1;
			wv3.v = pv1;
			verts.Add(wv3);

			verts.Add(wv1);
			verts.Add(wv4);
		}

		private unsafe static Bitmap CreateVoxelTexture(PixelColor[] palette) 
		{
			Bitmap bmp = new Bitmap(16, 16);
			BitmapData bmpdata = bmp.LockBits(new Rectangle(0, 0, 16, 16), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

			if(bmpdata != null) 
			{
				PixelColor* pixels = (PixelColor*)(bmpdata.Scan0.ToPointer());
				const int numpixels = 256;
				int i = 255;

				for(PixelColor* cp = pixels + numpixels - 1; cp >= pixels; cp--, i--) 
				{
					cp->r = palette[i].r;
					cp->g = palette[i].g;
					cp->b = palette[i].b;
					cp->a = palette[i].a;
				}
				bmp.UnlockBits(bmpdata);
			}

			//scale bitmap, so colors stay (almost) the same when bilinear filtering is enabled
			Bitmap scaled = new Bitmap(64, 64);
			using(Graphics gs = Graphics.FromImage(scaled)) 
			{
				gs.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
				gs.DrawImage(bmp, new Rectangle(0, 0, 64, 64), new Rectangle(0, 0, 16, 16), GraphicsUnit.Pixel);
			}
			bmp.Dispose();

			return scaled;
		}

		#endregion

		#region ================== Utility

		private static MemoryStream LoadFile(List<DataReader> containers, string path, bool isModel) 
		{
			foreach(DataReader dr in containers) 
			{
				if(isModel && dr is WADReader) continue;  //models cannot be stored in WADs

				//load file
				if(dr.FileExists(path)) return dr.LoadFile(path);
			}
			return null;
		}

		private static Texture LoadTexture(List<DataReader> containers, string path, Device device) 
		{
			if(string.IsNullOrEmpty(path)) return null;

			MemoryStream ms = LoadFile(containers, path, false);
			if(ms == null) return null;

			Texture texture = null;

			//create texture
			if(Path.GetExtension(path) == ".pcx") //pcx format requires special handling...
			{ 
				FileImageReader fir = new FileImageReader();
				Bitmap bitmap = fir.ReadAsBitmap(ms);

				ms.Close();

				if(bitmap != null) 
				{
					BitmapData bmlock = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
					texture = new Texture(device, bitmap.Width, bitmap.Height, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);

					DataRectangle textureLock = texture.LockRectangle(0, LockFlags.None);
					textureLock.Data.WriteRange(bmlock.Scan0, bmlock.Height * bmlock.Stride);

					bitmap.UnlockBits(bmlock);
					texture.UnlockRectangle(0);
				}
			} 
			else 
			{
				texture = Texture.FromStream(device, ms);

				ms.Close();
			}

			return texture;
		}

		private static void CreateMesh(Device device, ref MD3LoadResult result, List<WorldVertex> verts, List<int> indices)
		{
			//create mesh
			Mesh mesh = new Mesh(device, indices.Count / 3, verts.Count, MeshFlags.Use32Bit | MeshFlags.IndexBufferManaged | MeshFlags.VertexBufferManaged, vertexElements);

			using(DataStream stream = mesh.LockVertexBuffer(LockFlags.None))
			{
				stream.WriteRange(verts.ToArray());
			}
			mesh.UnlockVertexBuffer();

			using(DataStream stream = mesh.LockIndexBuffer(LockFlags.None))
			{
				stream.WriteRange(indices.ToArray());
			}
			mesh.UnlockIndexBuffer();

			mesh.OptimizeInPlace(MeshOptimizeFlags.AttributeSort);

			//store in result
			result.Meshes.Add(mesh);
		}

		private static string ReadString(BinaryReader br, int len) 
		{
			string result = string.Empty;
			int i;

			for(i = 0; i < len; ++i) 
			{
				var c = br.ReadChar();
				if(c == '\0') 
				{
					++i;
					break;
				}
				result += c;
			}

			for(; i < len; ++i) br.ReadChar();
			return result;
		}

		#endregion
	}
}
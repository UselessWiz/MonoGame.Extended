using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame.Extended.Tiled.Renderers
{
    public class TiledMapModelBuilder
    {
        private readonly GraphicsDevice _graphicsDevice;

        public TiledMapModelBuilder(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
        }

        private IEnumerable<TiledMapLayerModel> CreateLayerModels(TiledMap map, TiledMapLayer layer)
        {
            switch (layer)
            {
                case TiledMapTileLayer tileLayer:
                    return CreateTileLayerModels(map, tileLayer);
                case TiledMapImageLayer imageLayer:
                    return CreateImageLayerModels(imageLayer);
                default:
                    return new List<TiledMapLayerModel>();
            }

        }

        private IEnumerable<TiledMapLayerModel> CreateImageLayerModels(TiledMapImageLayer imageLayer)
        {
            var modelBuilder = new TiledMapStaticLayerModelBuilder();
            modelBuilder.AddSprite(imageLayer.Image, new Vector3(imageLayer.Position, 0), imageLayer.Image.Bounds, TiledMapTileFlipFlags.None);
            yield return modelBuilder.Build(_graphicsDevice, imageLayer.Image);
        }

        private IEnumerable<TiledMapLayerModel> CreateTileLayerModels(TiledMap map, TiledMapTileLayer tileLayer)
        {
            var layerModels = new List<TiledMapLayerModel>();
            var staticLayerBuilder = new TiledMapStaticLayerModelBuilder();
            var animatedLayerBuilder = new TiledMapAnimatedLayerModelBuilder();

            foreach (var tileset in map.Tilesets)
            {
                var firstGlobalIdentifier = map.GetTilesetFirstGlobalIdentifier(tileset);
                var lastGlobalIdentifier = tileset.TileCount + firstGlobalIdentifier - 1;
                var texture = tileset.Texture;

                foreach (var tile in tileLayer.Tiles.Where(t => firstGlobalIdentifier <= t.GlobalIdentifier && t.GlobalIdentifier <= lastGlobalIdentifier))
                {
                    var tileGid = tile.GlobalIdentifier;
                    var localTileIdentifier = tileGid - firstGlobalIdentifier;
                    var position = GetTilePosition(map, tile);
                    var sourceRectangle = tileset.GetTileRegion(localTileIdentifier);
                    var flipFlags = tile.Flags;

                    // Calculates the depth of the tile for walls higher than y=1, or sets to 1 for any other tilemap layers.
                    float tileDepth = 1f;

                    if (tileLayer.Name == "Interactable")
                    {
                        tileDepth = InteractableTileDepthCheck(map, tileLayer, tile);
                    }

                    // animated tiles
                    var tilesetTile = tileset.Tiles.FirstOrDefault(x => x.LocalTileIdentifier == localTileIdentifier);
                    if (tilesetTile?.Texture is not null)
                    {
                        position.Y += map.TileHeight - sourceRectangle.Height;
                        texture = tilesetTile.Texture;
                    }

                    if (tilesetTile is TiledMapTilesetAnimatedTile animatedTilesetTile)
                    {
                        animatedLayerBuilder.AddSprite(texture, new Vector3(position, tileDepth), sourceRectangle, flipFlags);
                        animatedTilesetTile.CreateTextureRotations(tileset, flipFlags);
                        animatedLayerBuilder.AnimatedTilesetTiles.Add(animatedTilesetTile);
                        animatedLayerBuilder.AnimatedTilesetFlipFlags.Add(flipFlags);

                        if (animatedLayerBuilder.IsFull)
                            layerModels.Add(animatedLayerBuilder.Build(_graphicsDevice, texture));
                    }
                    else
                    {
                        staticLayerBuilder.AddSprite(texture, new Vector3(position, tileDepth), sourceRectangle, flipFlags);

                        if (staticLayerBuilder.IsFull)
                            layerModels.Add(staticLayerBuilder.Build(_graphicsDevice, texture));
                    }
                }

                if (staticLayerBuilder.IsBuildable)
                    layerModels.Add(staticLayerBuilder.Build(_graphicsDevice, texture));

                if (animatedLayerBuilder.IsBuildable)
                    layerModels.Add(animatedLayerBuilder.Build(_graphicsDevice, texture));
            }

            return layerModels;
        }

        public TiledMapModel Build(TiledMap map)
        {
            var dictionary = new Dictionary<TiledMapLayer, TiledMapLayerModel[]>();
            foreach (var layer in map.Layers)
                BuildLayer(map, layer, dictionary);

            return new TiledMapModel(map, dictionary);
        }

        private void BuildLayer(TiledMap map, TiledMapLayer layer, Dictionary<TiledMapLayer, TiledMapLayerModel[]> dictionary)
        {
            if (layer is TiledMapGroupLayer groupLayer)
                foreach (var subLayer in groupLayer.Layers)
                    BuildLayer(map, subLayer, dictionary);
            else
                dictionary.Add(layer, CreateLayerModels(map, layer).ToArray());
        }

        private static Vector2 GetTilePosition(TiledMap map, TiledMapTile mapTile)
        {
            switch (map.Orientation)
            {
                case TiledMapOrientation.Orthogonal:
                    return TiledMapHelper.GetOrthogonalPosition(mapTile.X, mapTile.Y, map.TileWidth, map.TileHeight);
                case TiledMapOrientation.Isometric:
                    return TiledMapHelper.GetIsometricPosition(mapTile.X, mapTile.Y, map.TileWidth, map.TileHeight);
                default:
                    throw new NotSupportedException($"{map.Orientation} Tiled Maps are not yet implemented.");
            }
        }

        // Recursively walks down until it finds the lowest tile in the wall and uses that depth.
        // This process could be made better by calculating the depth and storing it, but considering
        // this calculation is only run once when the tilemap is loaded and built, it's not a big deal.
        private static float InteractableTileDepthCheck(TiledMap map, TiledMapTileLayer tileLayer, TiledMapTile tile)
        {
            TiledMapTile lowerTile = tileLayer.GetTile(tile.X, (ushort)(tile.Y + 1));

            // Check if there's a tile below this tile, and keep the depth of that tile
            if (lowerTile.GlobalIdentifier != 0)
            {
                // GID of 0 means cell is empty, therefore there is a tile below: https://doc.mapeditor.org/en/stable/reference/global-tile-ids/#mapping-a-gid-to-a-local-tile-id
                return InteractableTileDepthCheck(map, tileLayer, lowerTile);
            }
            else return 0.75f - (0.5f * GetTilePosition(map, tile).Y / map.HeightInPixels);
        }
    }
}

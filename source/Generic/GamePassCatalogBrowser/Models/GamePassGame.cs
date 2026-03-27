using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK.Data;

namespace GamePassCatalogBrowser.Models
{
    public class GamePassGame
    {
        public string BackgroundImage { get; set; }
        public string BackgroundImageUrl { get; set; }
        public string Category { get; set; }
        public List<string> Categories { get; set; }
        public string CoverImage { get; set; }
        public string CoverImageUrl { get; set; }
        public string CoverImageLowRes { get; set; }
        public string Description { get; set; }
        public List<string> Developers { get; set; }
        public string GameId { get; set; }
        public string Icon { get; set; }
        public string IconUrl { get; set; }
        public string Name { get; set; }
        public string ProductId { get; set; }
        public List<string> Publishers { get; set; }
        public ProductType ProductType { get; set; }
        public bool IsPC { get; set; }
        public bool IsConsole { get; set; }
        
        [DontSerialize]
        public string PlatformAndServiceLabel
        {
            get
            {
                var platforms = new List<string>();
                if (IsPC) platforms.Add("PC");
                if (IsConsole && !HideConsolePlatform) platforms.Add("Console");

                var service = ProductType == ProductType.EaGame ? "EA Play" : "Game Pass";

                if (platforms.Count == 0) 
                {
                    if (ProductType == ProductType.Collection)
                    {
                        return service;
                    }
                    return string.Empty;
                }

                return $"{string.Join(" | ", platforms)} • {service}";
            }
        }

        public DateTime ReleaseDate { get; set; }
        public bool IsChildProduct { get; set; }
        public string ParentProductId { get; set; }
        public List<string> ChildProducts { get; set; }

        [DontSerialize]
        public bool TempIsFound { get; set; }

        [DontSerialize]
        public bool HideConsolePlatform { get; set; }
    }

    public enum ProductType { Collection, Game, EaGame };
}
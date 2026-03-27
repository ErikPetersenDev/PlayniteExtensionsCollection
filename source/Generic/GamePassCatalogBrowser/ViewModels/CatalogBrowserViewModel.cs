using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Web;
using GamePassCatalogBrowser.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using System.Collections;
using PluginsCommon;

namespace GamePassCatalogBrowser.ViewModels
{
    class CatalogBrowserViewModel : INotifyPropertyChanged
    {
        private IPlayniteAPI PlayniteApi;
        private ICollectionView _gamePassGamesView;
        private ICollectionView _collectionsView;
        private ICollectionView _categoriesView;
        private string _collectionsFilterString;
        private string _categoriesFilterString;
        private string _searchString;
        private bool _storeButtonEnabled;
        private bool _addButtonEnabled;
        private XboxLibraryHelper xboxLibraryHelper;
        private GamePassCatalogBrowserSettings settings;

        private bool showGamesOnLibrary = true;
        public bool ShowGamesOnLibrary
        {
            get { return showGamesOnLibrary; }
            set
            {
                showGamesOnLibrary = value;
                _gamePassGamesView.Refresh();
            }
        }

        private GamePassGame selectedGamePassGame;
        public GamePassGame SelectedGamePassGame
        {
            get { return selectedGamePassGame; }
            set
            {
                selectedGamePassGame = value;
                NotifyPropertyChanged("SelectedGamePassGame");
                AddButtonEnabled = GetAddButtonStatus(value);
            }
        }

        public event Action RefreshRequested;

        public RelayCommand ClearSelectionCommand
        {
            get => new RelayCommand(() =>
            {
                SelectedGamePassGame = null;
            });
        }

        public RelayCommand RefreshCommand
        {
            get => new RelayCommand(() =>
            {
                RefreshRequested?.Invoke();
            });
        }

        public bool GetAddButtonStatus(GamePassGame game)
        {
            if (game == null)
            {
                return false;
            }
            if (xboxLibraryHelper.GameIdsInLibrary.Contains(game.GameId))
            {
                return false;
            }
            if (game.ProductType == ProductType.Collection)
            {
                return false;
            }
            if (!settings.SyncConsoleGamesToLibrary && game.IsConsole && !game.IsPC)
            {
                return false;
            }
            return true;
        }

        public bool StoreButtonEnabled
        {
            get { return _storeButtonEnabled; }
            set
            {
                _storeButtonEnabled = value;
            }
        }

        public bool AddButtonEnabled
        {
            get { return _addButtonEnabled; }
            set
            {
                _addButtonEnabled = value;
            }
        }

        public string SearchString
        {
            get { return _searchString; }
            set
            {
                _searchString = value;
                NotifyPropertyChanged("SearchString");
                _gamePassGamesView.Refresh();
            }
        }

        public ICollectionView GamePassGames
        {
            get { return _gamePassGamesView; }
        }

        public ICollectionView Collections
        {
            get { return _collectionsView; }
        }

        public ICollectionView Categories
        {
            get { return _categoriesView; }
        }

        public string CollectionsFilterString
        {
            get { return _collectionsFilterString; }
            set
            {
                _collectionsFilterString = value;
                NotifyPropertyChanged("CollectionsFilterString");
                _gamePassGamesView.Refresh();
            }
        }

        public string CategoriesFilterString
        {
            get { return _categoriesFilterString; }
            set
            {
                _categoriesFilterString = value;
                NotifyPropertyChanged("CategoriesFilterString");
                _gamePassGamesView.Refresh();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public CatalogBrowserViewModel(List<GamePassGame> list, IPlayniteAPI api, GamePassCatalogBrowserSettings pluginSettings)
        {
            PlayniteApi = api;
            settings = pluginSettings;
            _collectionsFilterString = "All PC";
            _categoriesFilterString = "All";

            xboxLibraryHelper = new XboxLibraryHelper(api);

            _gamePassGamesView = CollectionViewSource.GetDefaultView(list);
            _gamePassGamesView.CurrentChanged += (s, e) => { }; // Keep for consistency if needed
            _gamePassGamesView.Filter = GamePassGameFilter;

            IList<string> collections = GetCollectionsList();
            _collectionsView = CollectionViewSource.GetDefaultView(collections);
            _collectionsView.MoveCurrentTo("All PC");
            _collectionsView.CurrentChanged += CollectionSelectionChanged;

            IList<string> categories = GetCategoriesList(list);
            _categoriesView = CollectionViewSource.GetDefaultView(categories);
            _categoriesView.CurrentChanged += CategoriesSelectionChanged;
        }

        private void CollectionSelectionChanged(object sender, EventArgs e)
        {
            if (Collections.CurrentItem != null)
            {
                CollectionsFilterString = Collections.CurrentItem.ToString();
            }
        }

        private void CategoriesSelectionChanged(object sender, EventArgs e)
        {
            if (Categories.CurrentItem != null)
            {
                CategoriesFilterString = Categories.CurrentItem.ToString();
            }
        }

        private bool GamePassGameFilter(object item)
        {
            GamePassGame game = item as GamePassGame;
            if (game == null) return false;

            if (_collectionsFilterString != "All (PC and Console)")
            {
                switch (_collectionsFilterString)
                {
                    case "All PC":
                        if (!game.IsPC) return false;
                        break;
                    case "All Console":
                        if (!game.IsConsole) return false;
                        break;
                    case "PC: Xbox Game Pass":
                        if (!game.IsPC || game.ProductType != ProductType.Game) return false;
                        break;
                    case "PC: EA Play":
                        if (!game.IsPC || game.ProductType != ProductType.EaGame) return false;
                        break;
                    case "Console: Xbox Game Pass":
                        if (!game.IsConsole || game.ProductType != ProductType.Game) return false;
                        break;
                    case "Console: EA Play":
                        if (!game.IsConsole || game.ProductType != ProductType.EaGame) return false;
                        break;
                    case "Collections":
                        if (game.ProductType != ProductType.Collection) return false;
                        break;
                }
            }

            if (showGamesOnLibrary == false)
            {
                if (xboxLibraryHelper.GameIdsInLibrary.Contains(game.GameId))
                {
                    return false;
                }
            }

            return GameContainsString(game);
        }

        private bool GameContainsString(GamePassGame game)
        {
            if (string.IsNullOrEmpty(_searchString))
            {
                return IsGameInGenre(game);
            }
            
            if (game.Name.ToLower().Contains(_searchString.ToLower()))
            {
                return IsGameInGenre(game);
            }
            
            return false;
        }

        private bool IsGameInGenre(GamePassGame game)
        {
            if (_categoriesFilterString == "All")
            {
                return true;
            }
            
            if (string.IsNullOrEmpty(game.Category))
            {
                return false;
            }
            
            return game.Category == _categoriesFilterString;
        }

        private List<string> GetCollectionsList()
        {
            var collections = new List<string>();
            if (settings.ShowConsoleGamesInBrowser)
            {
                collections.Add("All (PC and Console)");
            }
            
            collections.Add("All PC");

            if (settings.ShowConsoleGamesInBrowser)
            {
                collections.Add("All Console");
            }
            
            collections.Add("PC: Xbox Game Pass");
            collections.Add("PC: EA Play");
            
            if (settings.ShowConsoleGamesInBrowser)
            {
                collections.Add("Console: Xbox Game Pass");
                collections.Add("Console: EA Play");
            }

            collections.Add("Collections");
            return collections;
        }

        private List<string> GetCategoriesList(List<GamePassGame> collection)
        {
            var categoriesList = new List<string> { "All" };
            var categoriesTempList = collection.Select(x => x.Category)
                .Where(a => a != null)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            categoriesList.AddRange(categoriesTempList);
            return categoriesList;
        }

        public RelayCommand<GamePassGame> StoreViewCommand
        {
            get => new RelayCommand<GamePassGame>((gamePassGame) =>
            {
                InvokeStoreView(gamePassGame);
            }, (gamePassGame) => gamePassGame != null);
        }

        private void InvokeStoreView(GamePassGame game)
        {
            if (game != null)
            {
                ProcessStarter.StartUrl($"ms-windows-store://pdp?productId={game.ProductId}");
            }
        }

        public RelayCommand<GamePassGame> XboxAppViewCommand
        {
            get => new RelayCommand<GamePassGame>((gamePassGame) =>
            {
                XboxApp(gamePassGame);
            }, (gamePassGame) => gamePassGame != null);
        }

        private void XboxApp(GamePassGame game)
        {
            if (game != null)
            {
                ProcessStarter.StartUrl($"msxbox://game/?productId={game.ProductId}");
            }
        }

        public RelayCommand<GamePassGame> AddGameToLibraryCommand
        {
            get => new RelayCommand<GamePassGame>((gamePassGame) =>
            {
                var success = false;
                success = xboxLibraryHelper.AddGameToLibrary(gamePassGame, true);
                if (success == true)
                {
                    AddButtonEnabled = false;
                    Collections.Refresh();
                }
            }, (gamePassGame) => AddButtonEnabled);
        }

        public void UpdateGamesList(List<GamePassGame> newList)
        {
            _gamePassGamesView = CollectionViewSource.GetDefaultView(newList);
            _gamePassGamesView.Filter = GamePassGameFilter;
            NotifyPropertyChanged("GamePassGames");
        }
    }
}
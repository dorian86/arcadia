using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Arcadia.Models;
using IOPath = System.IO.Path;

namespace Arcadia.Services;

/// <summary>
/// Developer tool (--make-demo &lt;folder&gt;): builds a demonstration library of fictional games with original,
/// procedurally drawn artwork (illustration, logo, cover, icon). Used for public screenshots: no personal
/// library and no artwork belonging to real games. Run on the UI thread.
/// </summary>
public static class DemoLibrary
{
    private sealed record Demo(string Name, string Studio, string Genres, int Year, GameStore Store, string Top, string Bottom,
        string Font, string Motif, double Hours, int DaysAgo, string Description, int Rating = 0, GameStatus Status = GameStatus.None,
        bool Favorite = false, string[]? Collections = null, string? Console = null);

    private static readonly Demo[] Games =
    {
        new("Nebula Drift", "Orbit Forge", "Course, Science-fiction", 2025, GameStore.Steam, "#2B1B6E", "#21C8E8", "Bahnschrift", "planet", 142, 0,
            "Des courses à pleine vitesse entre les anneaux d'une nébuleuse, en solo ou à huit en ligne.", 5, GameStatus.Playing, true, new[] { "Multijoueur" }),
        new("Les Lanternes de Brume", "Atelier Céladon", "Aventure, Narratif", 2024, GameStore.GOG, "#0E3B43", "#F2A65A", "Georgia", "forest", 38, 1,
            "Une enquête poétique dans un village où les lanternes gardent la mémoire des habitants.", 5, GameStatus.Finished, true),
        new("Neon Rally 2088", "Synthline", "Course, Arcade", 2023, GameStore.Steam, "#2A0A3F", "#FF3EA5", "Arial Black", "grid", 61, 2,
            "Rallye rétro-futuriste sur des autoroutes de néon, au rythme d'une bande-son synthwave.", 4, GameStatus.Playing, false, new[] { "Multijoueur" }),
        new("Le Dernier Phare", "Maison Écume", "Survie, Exploration", 2025, GameStore.Epic, "#0B2545", "#8DA9C4", "Cambria", "waves", 27, 3,
            "Gardien d'un phare isolé, vous devez tenir face aux tempêtes et aux secrets de l'océan.", 4),
        new("Kaiju Kitchen", "Petit Monstre", "Gestion, Coopératif", 2024, GameStore.Steam, "#5B1A12", "#FFB238", "Segoe UI Black", "city", 19, 5,
            "Cuisinez pour des monstres géants avant qu'ils ne détruisent la ville. À jouer à quatre.", 4, GameStatus.None, true, new[] { "Multijoueur", "Coop" }),
        new("Orbital Gardens", "Verdant Labs", "Construction, Relaxant", 2023, GameStore.Steam, "#0D3B2E", "#9BE564", "Bahnschrift", "planet", 88, 8,
            "Faites pousser des jardins suspendus en orbite et gérez une station autonome.", 5, GameStatus.Playing),
        new("Échos de Sylvane", "Atelier Céladon", "RPG, Fantastique", 2022, GameStore.GOG, "#1E1B4B", "#A78BFA", "Georgia", "forest", 104, 12,
            "Un grand RPG où chaque forêt chante une mélodie différente, à apprendre pour progresser.", 5, GameStatus.Finished, true),
        new("Starlight Courier", "Orbit Forge", "Action, Livraison", 2024, GameStore.Epic, "#101935", "#FFD166", "Bahnschrift", "planet", 12, 15,
            "Livrez des colis à travers la galaxie en évitant pirates et trous noirs.", 3),
        new("Duel des Marées", "Maison Écume", "Stratégie, Cartes", 2023, GameStore.Steam, "#062C43", "#4CC9F0", "Cambria", "waves", 45, 18,
            "Un jeu de cartes stratégique où chaque tour fait monter ou descendre la mer.", 4, GameStatus.None, false, new[] { "Multijoueur" }),
        new("Mecha Mosaic", "Gearbox Garden", "Action, Robots", 2025, GameStore.Steam, "#2D2D2D", "#F25F5C", "Arial Black", "city", 9, 21,
            "Assemblez votre robot pièce par pièce et défendez la ville contre les machines rebelles.", 0, GameStatus.Backlog),
        new("Tempête Sauvage", "Nordlys", "Action, Aventure", 2022, GameStore.Steam, "#1B263B", "#E0E1DD", "Impact", "mountains", 55, 26,
            "Traversez des terres glacées au cœur d'une tempête éternelle.", 4, GameStatus.Abandoned),
        new("Foxfire Trails", "Petit Monstre", "Plateformes", 2021, GameStore.GOG, "#3D1308", "#F28F3B", "Segoe UI Black", "forest", 23, 33,
            "Un renard lumineux, des forêts enchantées et des sauts au pixel près.", 4, GameStatus.Finished),
        new("Sky Garden Tycoon", "Verdant Labs", "Gestion", 2024, GameStore.Epic, "#14532D", "#86EFAC", "Bahnschrift", "mountains", 0, -1,
            "Bâtissez un empire de jardins flottants et attirez les touristes du monde entier.", 0, GameStatus.Backlog),
        new("Rogue Comet", "Synthline", "Roguelite, Action", 2025, GameStore.Steam, "#1A0B2E", "#C77DFF", "Impact", "planet", 33, 40,
            "Chaque course sur la comète est différente. Mourez, recommencez, devenez plus fort.", 5, GameStatus.Playing, true),
        new("Minuit à Verlaine", "Studio Bruine", "Enquête, Noir", 2023, GameStore.Steam, "#111827", "#F59E0B", "Georgia", "city", 14, 47,
            "Une enquête sous la pluie dans une ville où personne ne dort jamais.", 4),
        new("Bastion Céleste", "Nordlys", "Stratégie, Tower defense", 2022, GameStore.Steam, "#172554", "#60A5FA", "Cambria", "mountains", 71, 55,
            "Défendez la dernière citadelle des nuages contre des vagues de créatures.", 3, GameStatus.None, false, new[] { "Coop" }),
        new("Turbo Tortue", "Petit Monstre", "Course, Famille", 2021, GameStore.Local, "#14532D", "#FACC15", "Segoe UI Black", "grid", 6, 70,
            "La course la plus lente du monde... jusqu'à ce que les tortues trouvent le turbo.", 3, GameStatus.None, false, new[] { "Multijoueur" }),
        new("Voxel Village", "Cubic Kids", "Bac à sable, Construction", 2020, GameStore.Local, "#0C4A6E", "#7DD3FC", "Bahnschrift", "mountains", 120, 90,
            "Construisez, explorez et partagez votre village cube par cube.", 4, GameStatus.None, false, new[] { "Coop" }),
        new("Abyssal Bloom", "Maison Écume", "Exploration, Sous-marin", 2025, GameStore.Epic, "#042F2E", "#2DD4BF", "Cambria", "waves", 0, -1,
            "Plongez dans les abysses à la recherche de fleurs bioluminescentes.", 0, GameStatus.Backlog),
        new("Clockwork Odyssey", "Gearbox Garden", "Puzzle, Steampunk", 2019, GameStore.GOG, "#3B2F2F", "#D4A373", "Georgia", "city", 17, 120,
            "Réparez une ville mécanique engrenage par engrenage.", 4, GameStatus.Finished),
        new("Lune Rouge", "Studio Bruine", "Horreur, Survie", 2024, GameStore.Steam, "#1F0A0A", "#DC2626", "Impact", "forest", 8, 140,
            "Une nuit sous la lune rouge. Survivez jusqu'à l'aube.", 3),
        new("Pixel Paddle Party", "Cubic Kids", "Arcade, Rétro", 1996, GameStore.Emulator, "#1E1E3F", "#F472B6", "Consolas", "grid", 4, 160,
            "Le classique du tennis de table en pixels, à quatre sur le même écran.", 4, GameStatus.None, false, new[] { "Rétro", "Multijoueur" }, "Super Nintendo"),
        new("Galaxy Pinball DX", "Synthline", "Flipper, Rétro", 1998, GameStore.Emulator, "#0F172A", "#38BDF8", "Consolas", "planet", 3, 200,
            "Un flipper spatial aux tables infinies.", 3, GameStatus.None, false, new[] { "Rétro" }, "Nintendo 64"),
    };

    public static void Build(string folder)
    {
        AppPaths.Initialize(folder);
        var library = new LibraryService();
        var rnd = new Random(2088);
        var now = DateTime.Now;
        foreach (var d in Games)
        {
            var slug = new string(d.Name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Replace(' ', '-');
            var top = (Color)ColorConverter.ConvertFromString(d.Top);
            var bottom = (Color)ColorConverter.ConvertFromString(d.Bottom);
            var seed = d.Name.Aggregate(17, (h, c) => h * 31 + c);

            var game = new Game
            {
                Name = d.Name,
                NameLocked = true,
                LaunchTarget = $"demo://{slug}",
                SourcePath = $"demo://{slug}",
                Store = d.Store,
                Console = d.Console,
                Developer = d.Studio,
                Publisher = d.Studio,
                Genres = d.Genres,
                ReleaseYear = d.Year,
                Description = d.Description,
                MetadataState = MetadataState.Manual,
                IsFavorite = d.Favorite,
                Rating = d.Rating,
                Status = d.Status,
                Collections = d.Collections?.ToList() ?? new List<string>(),
                AccentColor = $"#{bottom.R:X2}{bottom.G:X2}{bottom.B:X2}",
                AddedAt = now.AddDays(-(d.DaysAgo < 0 ? rnd.Next(1, 4) : d.DaysAgo + rnd.Next(10, 60))),
            };

            var logo = Logo(d.Name, d.Font, bottom);
            game.Logo = Save(game, "logo", Png(logo));
            game.Hero = Save(game, "hero", Jpg(Scene(1920, 620, top, bottom, seed, d.Motif)));
            game.Cover = Save(game, "cover", Jpg(Cover(top, bottom, seed, d.Motif, logo, d.Studio)));
            game.Icon = Save(game, "icon", Png(Icon(top, bottom, d.Name)));

            // play history: sessions over the last weeks (statistics page, "recently played")
            if (d.Hours > 0 && d.DaysAgo >= 0)
            {
                var total = (long)(d.Hours * 3600);
                var sessions = new List<PlaySession>();
                long left = total;
                var day = d.DaysAgo;
                while (left > 0)
                {
                    var length = Math.Min(left, rnd.Next(25, 190) * 60L);
                    var start = now.Date.AddDays(-day).AddHours(rnd.Next(14, 23)).AddMinutes(rnd.Next(0, 59));
                    if (start > now) start = now.AddHours(-3);
                    sessions.Add(new PlaySession { Start = start, Seconds = length });
                    left -= length;
                    day += rnd.Next(1, 4);
                }
                sessions.Reverse();
                game.Sessions = sessions;
                game.PlayTimeSeconds = total;
                game.PlayCount = sessions.Count;
                game.LastPlayedAt = sessions[^1].Start.AddSeconds(sessions[^1].Seconds);
            }

            // tiles of the console themes, built right away
            var sources = new TileSources(game.CoverFullPath, game.HeroFullPath, game.LogoFullPath);
            game.TileSquare = Save(game, "tile_sq", TileArt.Square(sources));
            game.TileWide = Save(game, "tile_wide", TileArt.Wide(sources));
            game.TileSignature = TileArt.Signature(sources);

            library.Add(game);
            Log.Info($"Demo game created: {d.Name}");
        }
        library.SaveNow();

        // quiet settings for screenshots: no store scan, no Steam import, no online lookups, no update check
        var settings = new SettingsService();
        settings.Settings.AutoDetectGames = false;
        settings.Settings.ImportSteamPlaytime = false;
        settings.Settings.AutoFetchMetadata = false;
        settings.Settings.AutoCheckUpdates = false;
        settings.Settings.SoundEnabled = false;
        settings.Settings.AutoPlayTrailers = false;
        settings.SaveNow();
    }

    private static string? Save(Game game, string kind, byte[]? bytes)
    {
        if (bytes == null) return null;
        var ext = bytes.Length > 3 && bytes[0] == 0x89 ? "png" : "jpg";
        var rel = IOPath.Combine("images", $"{game.Id}_{kind}.{ext}");
        File.WriteAllBytes(AppPaths.Resolve(rel)!, bytes);
        return rel;
    }

    // ------------------------------------------------------------------------------------
    // Artwork
    // ------------------------------------------------------------------------------------

    /// <summary>A landscape: sky gradient, stars, a planet or sun, a motif (mountains, city, waves…) and fog.</summary>
    private static FrameworkElement Scene(int w, int h, Color top, Color accent, int seed, string motif)
    {
        var rnd = new Random(seed);
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
        canvas.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Shade(top, 0.55), 0), new(top, 0.45), new(Mix(top, accent, 0.45), 0.8), new(Mix(top, accent, 0.7), 1),
        }, new Point(0, 0), new Point(0, 1));

        // stars
        for (var i = 0; i < (w * h) / 9000; i++)
        {
            var s = rnd.NextDouble() * 2.6 + 0.6;
            var star = new Ellipse { Width = s, Height = s, Fill = Brushes.White, Opacity = rnd.NextDouble() * 0.7 + 0.2 };
            Canvas.SetLeft(star, rnd.NextDouble() * w);
            Canvas.SetTop(star, rnd.NextDouble() * h * 0.6);
            canvas.Children.Add(star);
        }

        // planet / sun with a glow
        var size = Math.Min(w, h) * (motif == "planet" ? 0.75 : 0.42);
        var cx = w * (0.55 + rnd.NextDouble() * 0.3);
        var cy = h * (motif == "planet" ? 0.42 : 0.38);
        var glow = new Ellipse
        {
            Width = size * 2.2, Height = size * 2.2,
            Fill = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B)),
        };
        Canvas.SetLeft(glow, cx - size * 1.1);
        Canvas.SetTop(glow, cy - size * 1.1);
        canvas.Children.Add(glow);
        var body = new Ellipse
        {
            Width = size, Height = size,
            Fill = new LinearGradientBrush(Lighten(accent, 0.45), Shade(accent, 0.55), new Point(0.2, 0.1), new Point(0.9, 1)),
        };
        Canvas.SetLeft(body, cx - size / 2);
        Canvas.SetTop(body, cy - size / 2);
        canvas.Children.Add(body);
        if (motif == "planet")
        {
            var ring = new Ellipse
            {
                Width = size * 1.9, Height = size * 0.42, StrokeThickness = size * 0.045,
                Stroke = new LinearGradientBrush(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(200, 255, 255, 255), 0),
                RenderTransform = new RotateTransform(-14, size * 0.95, size * 0.21),
            };
            Canvas.SetLeft(ring, cx - size * 0.95);
            Canvas.SetTop(ring, cy - size * 0.21);
            canvas.Children.Add(ring);
        }

        switch (motif)
        {
            case "city": City(canvas, w, h, top, accent, rnd); break;
            case "waves": Waves(canvas, w, h, top, accent, rnd); break;
            case "grid": NeonGrid(canvas, w, h, top, accent, rnd); break;
            case "forest": Ridges(canvas, w, h, top, accent, rnd, 2); Forest(canvas, w, h, top, rnd); break;
            default: Ridges(canvas, w, h, top, accent, rnd, 3); break;
        }

        // fog at the bottom
        var fog = new Rectangle
        {
            Width = w, Height = h * 0.35,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, Shade(top, 0.3).R, Shade(top, 0.3).G, Shade(top, 0.3).B), 90),
        };
        Canvas.SetTop(fog, h * 0.65);
        canvas.Children.Add(fog);
        return canvas;
    }

    private static void Ridges(Canvas canvas, int w, int h, Color top, Color accent, Random rnd, int layers)
    {
        for (var layer = 0; layer < layers; layer++)
        {
            var baseY = h * (0.55 + layer * 0.12);
            var amp = h * (0.18 - layer * 0.03);
            var fig = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(0, baseY), false));
            var steps = 7 + layer * 2;
            for (var i = 1; i <= steps; i++)
            {
                var x = w * i / (double)steps;
                var peak = baseY - rnd.NextDouble() * amp;
                fig.Segments.Add(new QuadraticBezierSegment(new Point(x - w / (double)steps / 2, peak), new Point(x, baseY - amp * 0.3 * rnd.NextDouble()), true));
            }
            fig.Segments.Add(new LineSegment(new Point(w, h), false));
            var tone = Mix(Shade(top, 0.5 - layer * 0.12), Shade(accent, 0.35), 0.25);
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { fig }), Fill = new SolidColorBrush(tone) });
        }
    }

    private static void City(Canvas canvas, int w, int h, Color top, Color accent, Random rnd)
    {
        Ridges(canvas, w, h, top, accent, rnd, 1);
        double x = -20;
        var unit = Math.Max(w, h) / 40.0;
        while (x < w)
        {
            var bw = unit * (1.2 + rnd.NextDouble() * 2.2);
            var bh = h * (0.18 + rnd.NextDouble() * 0.38);
            var building = new Rectangle { Width = bw, Height = bh, Fill = new SolidColorBrush(Shade(top, 0.28)) };
            Canvas.SetLeft(building, x);
            Canvas.SetTop(building, h - bh);
            canvas.Children.Add(building);
            for (var wy = h - bh + unit * 0.6; wy < h - unit; wy += unit * 0.7)
                for (var wx = x + unit * 0.3; wx < x + bw - unit * 0.4; wx += unit * 0.55)
                    if (rnd.NextDouble() < 0.33)
                    {
                        var win = new Rectangle { Width = unit * 0.22, Height = unit * 0.3, Fill = new SolidColorBrush(Lighten(accent, 0.3)), Opacity = 0.85 };
                        Canvas.SetLeft(win, wx);
                        Canvas.SetTop(win, wy);
                        canvas.Children.Add(win);
                    }
            x += bw + unit * 0.15;
        }
    }

    private static void Waves(Canvas canvas, int w, int h, Color top, Color accent, Random rnd)
    {
        for (var layer = 0; layer < 4; layer++)
        {
            var baseY = h * (0.62 + layer * 0.09);
            var fig = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(0, baseY), false));
            var period = w / (2.5 + layer);
            var phase = rnd.NextDouble() * period;
            for (double x = 0; x <= w + 10; x += 12)
                fig.Segments.Add(new LineSegment(new Point(x, baseY + Math.Sin((x + phase) / period * Math.PI * 2) * h * 0.025), true));
            fig.Segments.Add(new LineSegment(new Point(w, h), false));
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { fig }),
                Fill = new SolidColorBrush(Mix(Shade(top, 0.75 - layer * 0.15), accent, 0.18)),
                Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1.5,
            });
        }
    }

    private static void NeonGrid(Canvas canvas, int w, int h, Color top, Color accent, Random rnd)
    {
        var horizon = h * 0.6;
        canvas.Children.Add(new Rectangle { Width = w, Height = h - horizon, Fill = new SolidColorBrush(Shade(top, 0.35)) });
        Canvas.SetTop(canvas.Children[^1], horizon);
        var line = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
        for (var i = -12; i <= 12; i++)
            canvas.Children.Add(new Line { X1 = w / 2.0 + i * w * 0.02, Y1 = horizon, X2 = w / 2.0 + i * w * 0.22, Y2 = h, Stroke = line, StrokeThickness = 2 });
        for (var k = 1; k < 10; k++)
        {
            var y = horizon + (h - horizon) * Math.Pow(k / 10.0, 1.8);
            canvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = line, StrokeThickness = 1.6 });
        }
    }

    private static void Forest(Canvas canvas, int w, int h, Color top, Random rnd)
    {
        var unit = Math.Max(w, h) / 30.0;
        for (var row = 0; row < 2; row++)
        {
            double x = -unit;
            var fill = new SolidColorBrush(Shade(top, 0.32 - row * 0.12));
            while (x < w + unit)
            {
                var th = unit * (2.2 + rnd.NextDouble() * 2.4) * (1 + row * 0.3);
                var tw = th * 0.42;
                var baseY = h - row * unit * 0.2 + unit * 0.4;
                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection { new(x, baseY), new(x + tw / 2, baseY - th), new(x + tw, baseY) },
                    Fill = fill,
                });
                x += tw * (0.55 + rnd.NextDouble() * 0.4);
            }
        }
    }

    /// <summary>The title in a display font, gradient filled, outlined, on a transparent background.</summary>
    private static FrameworkElement Logo(string name, string font, Color accent)
    {
        var text = new FormattedText(name.ToUpperInvariant(), CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 120, Brushes.White, 1.0);
        if (name.Length > 16) text.MaxTextWidth = 1300;
        text.TextAlignment = TextAlignment.Center;
        var geometry = text.BuildGeometry(new Point(0, 0));
        var b = geometry.Bounds;
        var pad = 24;
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = new LinearGradientBrush(Colors.White, Lighten(accent, 0.25), 90),
            Stroke = new SolidColorBrush(Shade(accent, 0.25)), StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.7, Color = Colors.Black },
        };
        var canvas = new Canvas { Width = b.Width + pad * 2, Height = b.Height + pad * 2 };
        Canvas.SetLeft(path, pad - b.Left);
        Canvas.SetTop(path, pad - b.Top);
        canvas.Children.Add(path);
        return canvas;
    }

    private static FrameworkElement Cover(Color top, Color accent, int seed, string motif, FrameworkElement logo, string studio)
    {
        var root = new Grid { Width = 600, Height = 900 };
        root.Children.Add(Scene(600, 900, top, accent, seed, motif));
        var logoImage = new Image { Source = Bitmap(logo), Stretch = Stretch.Uniform, MaxWidth = 520, MaxHeight = 260, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, 0, 0) };
        root.Children.Add(logoImage);
        root.Children.Add(new TextBlock
        {
            Text = studio.ToUpperInvariant(), Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), FontSize = 20,
            FontFamily = new FontFamily("Bahnschrift"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 34),
        });
        return root;
    }

    private static FrameworkElement Icon(Color top, Color accent, string name)
    {
        var root = new Grid { Width = 256, Height = 256 };
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(56),
            Background = new LinearGradientBrush(Lighten(accent, 0.1), Shade(top, 0.9), 45),
        });
        root.Children.Add(new TextBlock
        {
            Text = name[..1].ToUpperInvariant(), FontSize = 150, FontWeight = FontWeights.Black, Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Black"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return root;
    }

    // ------------------------------------------------------------------------------------
    // Rendering helpers
    // ------------------------------------------------------------------------------------

    private static BitmapSource Bitmap(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = (int)Math.Ceiling(e.Width > 0 ? e.Width : e.DesiredSize.Width);
        var h = (int)Math.Ceiling(e.Height > 0 ? e.Height : e.DesiredSize.Height);
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(e);
        rtb.Freeze();
        return rtb;
    }

    private static byte[] Png(FrameworkElement e) => Encode(new PngBitmapEncoder(), Bitmap(e));

    private static byte[] Jpg(FrameworkElement e) => Encode(new JpegBitmapEncoder { QualityLevel = 92 }, Bitmap(e));

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource bmp)
    {
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

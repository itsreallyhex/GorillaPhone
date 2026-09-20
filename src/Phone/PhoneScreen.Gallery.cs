using System;
using System.Collections.Generic;
using GorillaPhone.Photo;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The Gallery app: a scrolling grid of every photo in the photo folder (old and new), a full-screen
    /// viewer with the date, the camera and the map, and a delete button with a confirmation.
    /// The grid scrolls with the arrow buttons and by dragging a fingertip pushed into it; a tap selects a
    /// photo and a double tap opens it. Photos are decoded on a background thread (PhotoWorker) and only
    /// small textures reach the main thread. "Delete" moves the file into a Deleted folder next to the photos.
    /// </summary>
    public sealed partial class PhoneScreen
    {
        // How far behind the screen a fingertip can be and still count as touching (dragging).
        const float TouchMaxBehind = 0.08f;
        const float TouchRelease = 0.02f; // a touch ends when the fingertip is this far in front again
        const float TapSlop = 0.015f;     // a touch that stays within 2 cm of where it began is a tap, not a drag
        const float TapMaxTime = 0.8f;    // and it must be over within this many seconds
        const float DoubleTapTime = 0.8f; // the second tap on the same photo must come within this

        const int Cols = 2;
        const int PoolRows = 5;      // rows of tiles kept alive; the grid re-uses them as it scrolls
        const int ThumbHeight = 320; // decode size for grid tiles
        const int ViewHeight = 960;  // decode size for the viewer
        const int MaxThumbs = 48;    // thumbnails kept in memory

        sealed class Tile
        {
            public Transform Node;
            public Part Frame, Photo, Strip;
            public TextMeshPro Label;
            public Texture2D Tex;
            public Vector2 CropScale = Vector2.one, CropOffset = Vector2.zero;   // how the whole photo fills the whole tile
            public int Index = -1;
        }

        sealed class Thumb
        {
            public byte[] Rgba;   // first row = bottom, ready for a Unity texture
            public int W, H;
            public long Used;
        }

        struct TouchInfo
        {
            public Vector2 Start, Last;
            public float T0, Vy;
            public bool Left, Moving;   // Moving: the finger left the tap dead zone, so this touch is a drag
        }

        // ---- shared
        Transform galRoot, viewRoot, confirmRoot;
        PhotoWorker worker;
        readonly List<PhotoInfo> photos = new List<PhotoInfo>();
        readonly Dictionary<string, Thumb> thumbs = new Dictionary<string, Thumb>();
        readonly HashSet<string> failed = new HashSet<string>();
        long useClock;
        bool scanning, scanned;
        int selected = -1, viewIndex = -1;

        // ---- grid page
        Part galBg, galHeader, galFooter, galHomePart, upPart, downPart;
        TextMeshPro galTitle, galCount, galEmpty, galEmpty2;
        Button galHomeBtn, upBtn, downBtn;
        readonly Tile[] tiles = new Tile[Cols * PoolRows];
        float gSw, gTop, gBottom, tileW, tileH, gap, rowH, colX, stripH;
        float scroll, scrollVel, scrollTarget = -1f, maxScroll;
        bool dragging;
        int lastTapIndex = -1;
        float lastTapTime;

        // ---- touch
        readonly bool[] touching = new bool[2];
        readonly TouchInfo[] touch = new TouchInfo[2];

        // ---- viewer page
        Part vBg, vHeader, vFooter, vPhoto, gridPart, prevPart, nextPart, trashPart;
        TextMeshPro vCount, vLine1, vLine2;
        Button backBtn, prevBtn, nextBtn, delBtn;
        Texture2D viewTex;
        float vAreaCy, vAreaH;
        int viewW, viewH;
        bool lastPrevOk = true, lastNextOk = true;

        // ---- delete confirmation
        Part cDim, cCard, cCancel, cDelete;
        TextMeshPro cText, cSub, cCancelText, cDeleteText;
        Button cancelBtn, deleteBtn;

        // ------------------------------------------------------------------ build

        void BuildGallery()
        {
            worker = new PhotoWorker();
            galRoot = NewNode("Gallery", root);
            viewRoot = NewNode("Viewer", root);
            confirmRoot = NewNode("Confirm", root);

            Color bar = new Color(0.07f, 0.08f, 0.12f, 1f);
            Color bg = new Color(0.05f, 0.06f, 0.10f, 1f);

            // ---- grid page
            galBg = Opaque(galRoot, "Background", bg, null, 0);
            Transform tilesNode = NewNode("Tiles", galRoot);
            for (int i = 0; i < tiles.Length; i++) tiles[i] = MakeTile(tilesNode, i);
            galHeader = Opaque(galRoot, "Header", bar, null, 5);
            galFooter = Opaque(galRoot, "Footer", bar, null, 5);
            galTitle = PhoneText.Create(log, galRoot, "Title", "Gallery", 0.01f, 0.1f, TextColor, 30);
            galCount = PhoneText.Create(log, galRoot, "Count", "", 0.01f, 0.1f, DimText, 30);
            galEmpty = PhoneText.Create(log, galRoot, "Empty", "No photos yet", 0.01f, 0.1f, TextColor, 30);
            galEmpty2 = PhoneText.Create(log, galRoot, "Empty2", "Take one in the Camera app", 0.01f, 0.1f, DimText, 30);
            galHomePart = Glass(galRoot, "HomeButton", Color.white, Track(ShapeTextures.Home(128)), 10);
            upPart = Glass(galRoot, "Up", Color.white, Track(ShapeTextures.ArrowUp(128)), 10);
            downPart = Glass(galRoot, "Down", Color.white, Track(ShapeTextures.ArrowDown(128)), 10);
            galHomeBtn = AddButton(Page.Gallery, GoHome, 0.4f);
            upBtn = AddButton(Page.Gallery, delegate { ScrollBy(-2f); }, 0.2f);
            downBtn = AddButton(Page.Gallery, delegate { ScrollBy(2f); }, 0.2f);

            // ---- viewer page
            vBg = Opaque(viewRoot, "Background", bg, null, 0);
            vPhoto = Opaque(viewRoot, "Photo", Color.white, null, 2);
            vPhoto.T.gameObject.SetActive(false);
            vHeader = Opaque(viewRoot, "Header", bar, null, 5);
            vFooter = Opaque(viewRoot, "Footer", bar, null, 5);
            vCount = PhoneText.Create(log, viewRoot, "Count", "", 0.01f, 0.1f, DimText, 30);
            vLine1 = PhoneText.Create(log, viewRoot, "Line1", "", 0.01f, 0.1f, TextColor, 30);
            vLine2 = PhoneText.Create(log, viewRoot, "Line2", "", 0.01f, 0.1f, DimText, 30);
            gridPart = Glass(viewRoot, "BackButton", Color.white, Track(ShapeTextures.GridButton(128)), 10);
            prevPart = Glass(viewRoot, "Prev", Color.white, Track(ShapeTextures.ArrowLeft(128)), 10);
            nextPart = Glass(viewRoot, "Next", Color.white, Track(ShapeTextures.ArrowRight(128)), 10);
            trashPart = Glass(viewRoot, "Delete", Color.white, Track(ShapeTextures.Trash(128)), 10);
            backBtn = AddButton(Page.Viewer, CloseViewer, 0.4f);
            prevBtn = AddButton(Page.Viewer, delegate { StepViewer(-1); }, 0.25f);
            nextBtn = AddButton(Page.Viewer, delegate { StepViewer(1); }, 0.25f);
            delBtn = AddButton(Page.Viewer, AskDelete, 0.5f);

            // ---- delete confirmation, drawn over the viewer
            Texture2D pill = Track(ShapeTextures.Pill(192, 64));
            cDim = Glass(confirmRoot, "Dim", new Color(0f, 0f, 0f, 0.65f), null, 50);
            cCard = Glass(confirmRoot, "Card", new Color(0.12f, 0.13f, 0.18f, 1f), Track(ShapeTextures.Card(256, 140)), 51);
            cCancel = Glass(confirmRoot, "Cancel", new Color(0.30f, 0.32f, 0.38f, 1f), pill, 52);
            cDelete = Glass(confirmRoot, "DeleteYes", new Color(0.85f, 0.25f, 0.25f, 1f), pill, 52);
            cText = PhoneText.Create(log, confirmRoot, "Question", "Delete this photo?", 0.01f, 0.1f, TextColor, 60);
            cSub = PhoneText.Create(log, confirmRoot, "Note", "It moves to a Deleted folder.", 0.01f, 0.1f, DimText, 60);
            cCancelText = PhoneText.Create(log, confirmRoot, "CancelText", "Cancel", 0.01f, 0.1f, TextColor, 61);
            cDeleteText = PhoneText.Create(log, confirmRoot, "DeleteText", "Delete", 0.01f, 0.1f, TextColor, 61);
            cancelBtn = AddButton(Page.Confirm, CancelDelete, 0.4f);
            deleteBtn = AddButton(Page.Confirm, DoDelete, 1.0f);   // a long cooldown so a double poke cannot delete twice

            galRoot.gameObject.SetActive(false);
            viewRoot.gameObject.SetActive(false);
            confirmRoot.gameObject.SetActive(false);
        }

        Tile MakeTile(Transform parent, int i)
        {
            var t = new Tile();
            t.Node = NewNode("Tile" + i, parent);
            t.Frame = Opaque(t.Node, "Frame", Color.white, null, 1);
            t.Photo = Opaque(t.Node, "Photo", new Color(0.15f, 0.16f, 0.21f, 1f), null, 2);
            t.Strip = Glass(t.Node, "Strip", new Color(0f, 0f, 0f, 0.6f), null, 3);
            t.Label = PhoneText.Create(log, t.Node, "Label", "", 0.01f, 0.1f, TextColor, 30);
            t.Tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "GP_Tile", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            textures.Add(t.Tex);
            t.Node.gameObject.SetActive(false);
            return t;
        }

        void LayoutGallery(float sw, float sh, float margin)
        {
            if (galRoot == null) return;
            gSw = sw;
            float top = sh * 0.5f, bot = -sh * 0.5f;
            float hh = 0.27f * sw, fh = 0.22f * sw;   // header and footer bar heights
            gTop = top - hh;
            gBottom = bot + fh;
            float hy = top - 0.19f * sw;              // the header's row, below the island
            float bd = 0.13f * sw;

            // ---- grid page
            Place(galBg, 0f, 0f, sw, sh, -0.0004f);
            // The bars sit in front of the tiles (more negative z), so tiles scrolled under them are hidden.
            Place(galHeader, 0f, top - hh * 0.5f, sw, hh, -0.0016f);
            Place(galFooter, 0f, bot + fh * 0.5f, sw, fh, -0.0016f);
            PhoneText.Resize(galTitle, 0.06f * sw, 0.4f * sw);
            PhoneText.Place(galTitle, 0f, hy, -0.0022f);
            PhoneText.Resize(galCount, 0.04f * sw, 0.3f * sw);
            PhoneText.Place(galCount, 0.33f * sw, hy, -0.0022f);
            Place(galHomePart, -0.37f * sw, hy, bd, bd, -0.0022f);
            SetRect(galHomeBtn, -0.37f * sw, hy, bd, margin);
            float fy = bot + fh * 0.5f;
            Place(upPart, -0.14f * sw, fy, bd, bd, -0.0022f);
            Place(downPart, 0.14f * sw, fy, bd, bd, -0.0022f);
            SetRect(upBtn, -0.14f * sw, fy, bd, margin);
            SetRect(downBtn, 0.14f * sw, fy, bd, margin);
            PhoneText.Resize(galEmpty, 0.055f * sw, 0.8f * sw);
            PhoneText.Place(galEmpty, 0f, 0.06f * sw, -0.0022f);
            PhoneText.Resize(galEmpty2, 0.038f * sw, 0.9f * sw);
            PhoneText.Place(galEmpty2, 0f, -0.02f * sw, -0.0022f);

            tileW = 0.44f * sw;
            tileH = 0.50f * sw;
            gap = 0.04f * sw;
            rowH = tileH + gap;
            colX = (tileW + gap) * 0.5f;
            stripH = 0.075f * sw;
            for (int i = 0; i < tiles.Length; i++)
            {
                Tile t = tiles[i];
                Place(t.Frame, 0f, 0f, tileW + 0.012f * sw, tileH + 0.012f * sw, -0.0006f);
                Place(t.Photo, 0f, 0f, tileW, tileH, -0.0008f);
                Place(t.Strip, 0f, -tileH * 0.5f + stripH * 0.5f, tileW, stripH, -0.0010f);
                PhoneText.Resize(t.Label, 0.042f * sw, tileW * 0.95f);
                PhoneText.Place(t.Label, 0f, -tileH * 0.5f + stripH * 0.5f, -0.0012f);
                t.Index = -1;   // sizes changed: re-crop the photos
            }

            // ---- viewer page
            float vBar = 0.42f * sw;
            Place(vBg, 0f, 0f, sw, sh, -0.0004f);
            Place(vHeader, 0f, top - hh * 0.5f, sw, hh, -0.0016f);
            Place(vFooter, 0f, bot + vBar * 0.5f, sw, vBar, -0.0016f);
            Place(gridPart, -0.37f * sw, hy, bd, bd, -0.0022f);
            SetRect(backBtn, -0.37f * sw, hy, bd, margin);
            PhoneText.Resize(vCount, 0.04f * sw, 0.3f * sw);
            PhoneText.Place(vCount, 0.33f * sw, hy, -0.0022f);
            PhoneText.Resize(vLine1, 0.04f * sw, 0.96f * sw);
            PhoneText.Place(vLine1, 0f, bot + 0.335f * sw, -0.0022f);
            PhoneText.Resize(vLine2, 0.036f * sw, 0.96f * sw);
            PhoneText.Place(vLine2, 0f, bot + 0.27f * sw, -0.0022f);
            float by = bot + 0.105f * sw, vbd = 0.14f * sw;
            Place(prevPart, -0.30f * sw, by, vbd, vbd, -0.0022f);
            Place(trashPart, 0f, by, vbd, vbd, -0.0022f);
            Place(nextPart, 0.30f * sw, by, vbd, vbd, -0.0022f);
            SetRect(prevBtn, -0.30f * sw, by, vbd, margin);
            SetRect(delBtn, 0f, by, vbd, margin);
            SetRect(nextBtn, 0.30f * sw, by, vbd, margin);
            vAreaCy = (gTop + (bot + vBar)) * 0.5f;
            vAreaH = gTop - (bot + vBar);
            if (viewW > 0) PlaceViewerPhoto();

            // ---- delete confirmation
            Place(cDim, 0f, 0f, sw, sh, -0.0026f);
            Place(cCard, 0f, 0f, 0.84f * sw, 0.46f * sw, -0.0028f);
            PhoneText.Resize(cText, 0.06f * sw, 0.8f * sw);
            PhoneText.Place(cText, 0f, 0.12f * sw, -0.0034f);
            PhoneText.Resize(cSub, 0.036f * sw, 0.8f * sw);
            PhoneText.Place(cSub, 0f, 0.05f * sw, -0.0034f);
            float pw = 0.34f * sw, ph = pw / 3f, py = -0.12f * sw;
            Place(cCancel, -0.20f * sw, py, pw, ph, -0.0030f);
            Place(cDelete, 0.20f * sw, py, pw, ph, -0.0030f);
            PhoneText.Resize(cCancelText, 0.045f * sw, pw * 0.9f);
            PhoneText.Place(cCancelText, -0.20f * sw, py, -0.0034f);
            PhoneText.Resize(cDeleteText, 0.045f * sw, pw * 0.9f);
            PhoneText.Place(cDeleteText, 0.20f * sw, py, -0.0034f);
            cancelBtn.Center = new Vector2(-0.20f * sw, py);
            cancelBtn.Half = new Vector2(pw * 0.5f + margin, ph * 0.5f + margin);
            deleteBtn.Center = new Vector2(0.20f * sw, py);
            deleteBtn.Half = new Vector2(pw * 0.5f + margin, ph * 0.5f + margin);
        }

        // ------------------------------------------------------------------ pages

        void OnPageChanged(Page p)
        {
            if (p == Page.Gallery)
            {
                lastTapIndex = -1;
                RequestScan();   // pick up photos taken (or moved) since the last visit
            }
        }

        void RequestScan()
        {
            if (scanning || worker == null) return;
            scanning = true;
            worker.Scan(pcam.PhotoFolder(), OnScan);
        }

        void OnScan(List<PhotoInfo> list)
        {
            scanning = false;
            scanned = true;
            string keep = selected >= 0 && selected < photos.Count ? photos[selected].Path : null;
            photos.Clear();
            photos.AddRange(list);
            selected = -1;
            if (keep != null) selected = photos.FindIndex(p => p.Path == keep);
            for (int i = 0; i < tiles.Length; i++) tiles[i].Index = -1;   // reassign every tile
            log.LogInfo("gallery: " + photos.Count + " photo(s) in " + pcam.PhotoFolder());
        }

        string FormatDate(DateTime now)
        {
            // The PC's own long date format (its language and order) unless the player set a format.
            string f = cfg.DateFormat.Value;
            try { return string.IsNullOrEmpty(f) ? now.ToString("D") : now.ToString(f); }
            catch (FormatException) { return now.ToString("D"); }
        }

        // ------------------------------------------------------------------ grid

        void UpdateGallery()
        {
            float dt = Time.unscaledDeltaTime;
            int rows = (photos.Count + Cols - 1) / Cols;
            float viewportH = gTop - gBottom;
            maxScroll = Mathf.Max(0f, (rows * rowH + gap * 0.5f - viewportH) / rowH);

            if (!dragging)
            {
                if (scrollTarget >= 0f)
                {
                    // An arrow was pressed: glide to the row.
                    scroll = Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-12f * dt));
                    if (Mathf.Abs(scroll - scrollTarget) < 0.003f) { scroll = scrollTarget; scrollTarget = -1f; }
                }
                else if (Mathf.Abs(scrollVel) > 0.01f)
                {
                    // A drag was let go: keep moving and slow down.
                    scroll += scrollVel * dt;
                    scrollVel *= Mathf.Exp(-4f * dt);
                }
            }
            ClampScroll();
            RefreshTiles();

            PhoneText.Set(galCount, !scanned ? "..." : photos.Count == 1 ? "1 photo" : photos.Count + " photos");
            bool empty = scanned && photos.Count == 0;
            SetTextActive(galEmpty, empty);
            SetTextActive(galEmpty2, empty);
        }

        static void SetTextActive(TextMeshPro t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        void ClampScroll()
        {
            if (scroll < 0f) { scroll = 0f; scrollVel = 0f; }
            else if (scroll > maxScroll) { scroll = maxScroll; scrollVel = 0f; }
        }

        void ScrollBy(float rows)
        {
            float from = scrollTarget >= 0f ? scrollTarget : scroll;
            scrollTarget = Mathf.Clamp(Mathf.Round(from + rows), 0f, maxScroll);
            scrollVel = 0f;
        }

        void EnsureVisible(int index)
        {
            if (index < 0) return;
            int row = index / Cols;
            float visibleRows = (gTop - gBottom) / rowH;
            if (row < scroll) scroll = row;
            else if (row + 1 > scroll + visibleRows) scroll = row + 1 - visibleRows;
            scrollTarget = -1f;
            scrollVel = 0f;
        }

        /// <summary>Places the tiles for the current scroll position. A tile is only re-filled when it starts showing a different photo.</summary>
        void RefreshTiles()
        {
            int firstRow = (int)Mathf.Floor(scroll) - 1;
            for (int k = 0; k < PoolRows; k++)
            {
                int row = firstRow + k;
                int slot = ((row % PoolRows) + PoolRows) % PoolRows;   // five neighbouring rows always use five different slots
                for (int c = 0; c < Cols; c++)
                {
                    Tile t = tiles[slot * Cols + c];
                    int index = row * Cols + c;
                    if (row < 0 || index >= photos.Count)
                    {
                        if (t.Index != -1) { t.Index = -1; t.Node.gameObject.SetActive(false); }
                        continue;
                    }
                    if (t.Index != index) AssignTile(t, index);
                    float y = gTop - gap * 0.5f - (row - scroll) * rowH - tileH * 0.5f;
                    t.Node.localPosition = new Vector3(c == 0 ? -colX : colX, y, 0f);
                    bool sel = index == selected;
                    if (t.Frame.T.gameObject.activeSelf != sel) t.Frame.T.gameObject.SetActive(sel);
                    ClipTile(t, y);
                }
            }
        }

        /// <summary>
        /// Cuts a tile to the area between the header and the footer. Without this a tile scrolled past the
        /// screen's edge would still be drawn over the phone's bezel, and the bars only hide what is on the screen.
        /// The photo's quad is shortened and its texture crop trimmed to match, so the picture does not squash.
        /// </summary>
        void ClipTile(Tile t, float y)
        {
            float half = tileH * 0.5f, bottom = y - half;
            float lo = Mathf.Max(bottom, gBottom), hi = Mathf.Min(y + half, gTop);
            bool any = hi - lo > 0.0005f;
            if (t.Node.gameObject.activeSelf != any) t.Node.gameObject.SetActive(any);
            if (!any) return;

            // the photo: the visible part of the tile, with the matching part of the crop
            float v0 = (lo - bottom) / tileH, v1 = (hi - bottom) / tileH;
            Place(t.Photo, 0f, (lo + hi) * 0.5f - y, tileW, hi - lo, -0.0008f);
            PhoneMaterials.SetCrop(t.Photo.M,
                new Vector2(t.CropScale.x, t.CropScale.y * (v1 - v0)),
                new Vector2(t.CropOffset.x, t.CropOffset.y + t.CropScale.y * v0));

            // the selection frame: the same, with its border
            float b = 0.006f * gSw;
            float fLo = Mathf.Max(bottom - b, gBottom), fHi = Mathf.Min(y + half + b, gTop);
            Place(t.Frame, 0f, (fLo + fHi) * 0.5f - y, tileW + 2f * b, Mathf.Max(fHi - fLo, 0.0001f), -0.0006f);

            // the date strip at the tile's bottom; its label only shows when the whole strip does
            float sLo = Mathf.Max(bottom, gBottom), sHi = Mathf.Min(bottom + stripH, gTop);
            bool strip = sHi - sLo > 0.0005f;
            if (t.Strip.T.gameObject.activeSelf != strip) t.Strip.T.gameObject.SetActive(strip);
            if (strip) Place(t.Strip, 0f, (sLo + sHi) * 0.5f - y, tileW, sHi - sLo, -0.0010f);
            SetTextActive(t.Label, strip && sHi - sLo >= stripH - 0.0005f);
        }

        void AssignTile(Tile t, int index)
        {
            t.Index = index;
            PhotoInfo p = photos[index];
            PhoneText.Set(t.Label, p.Time.ToString("d"));   // the PC's short date format
            PhoneMaterials.SetTexture(t.Photo.M, null);
            t.CropScale = Vector2.one;
            t.CropOffset = Vector2.zero;   // RefreshTiles applies the crop (trimmed to what is visible) every frame
            PhoneMaterials.SetColor(t.Photo.M, new Color(0.15f, 0.16f, 0.21f, 1f));

            Thumb th;
            if (thumbs.TryGetValue(p.Path, out th)) UploadTile(t, th);
            else if (failed.Contains(p.Path)) PhoneText.Set(t.Label, "can't open");
            else worker.Load(p.Path, ThumbHeight, OnLoaded);
        }

        void UploadTile(Tile t, Thumb th)
        {
            if (t.Tex.width != th.W || t.Tex.height != th.H) t.Tex.Reinitialize(th.W, th.H);
            t.Tex.LoadRawTextureData(th.Rgba);
            t.Tex.Apply(false);
            PhoneMaterials.SetTexture(t.Photo.M, t.Tex);
            PhoneMaterials.SetColor(t.Photo.M, Color.white);

            // Fill the tile: crop the photo's top and bottom (or sides) to the tile's shape.
            float pa = (float)th.W / th.H, ta = tileW / tileH;
            if (pa < ta) { float f = pa / ta; t.CropScale = new Vector2(1f, f); t.CropOffset = new Vector2(0f, (1f - f) * 0.5f); }
            else { float f = ta / pa; t.CropScale = new Vector2(f, 1f); t.CropOffset = new Vector2((1f - f) * 0.5f, 0f); }
            th.Used = ++useClock;
        }

        /// <summary>Called on the main thread when the worker has decoded a photo (a grid thumbnail or the viewer's bigger picture).</summary>
        void OnLoaded(LoadResult r)
        {
            if (r.Error != null)
            {
                if (failed.Add(r.Path)) log.LogWarning("gallery: cannot open " + r.Path + ": " + r.Error);
                foreach (Tile t in tiles)
                    if (t.Index >= 0 && t.Index < photos.Count && photos[t.Index].Path == r.Path) PhoneText.Set(t.Label, "can't open");
                if (viewIndex >= 0 && viewIndex < photos.Count && photos[viewIndex].Path == r.Path && r.TargetHeight == ViewHeight)
                    PhoneText.Set(vLine2, "This photo can't be shown");
                return;
            }

            if (r.TargetHeight == ThumbHeight)
            {
                var th = new Thumb { Rgba = r.Rgba, W = r.Width, H = r.Height, Used = ++useClock };
                thumbs[r.Path] = th;
                if (thumbs.Count > MaxThumbs) EvictOldestThumb();
                foreach (Tile t in tiles)
                    if (t.Index >= 0 && t.Index < photos.Count && photos[t.Index].Path == r.Path) UploadTile(t, th);
                // The viewer shows this small version while its big one is still loading.
                if (viewIndex >= 0 && viewIndex < photos.Count && photos[viewIndex].Path == r.Path && viewW == 0) UploadViewer(r.Rgba, r.Width, r.Height);
            }
            else if (viewIndex >= 0 && viewIndex < photos.Count && photos[viewIndex].Path == r.Path)
            {
                UploadViewer(r.Rgba, r.Width, r.Height);
            }
        }

        void EvictOldestThumb()
        {
            string oldest = null;
            long best = long.MaxValue;
            foreach (KeyValuePair<string, Thumb> kv in thumbs)
                if (kv.Value.Used < best) { best = kv.Value.Used; oldest = kv.Key; }
            if (oldest != null) thumbs.Remove(oldest);
        }

        // ------------------------------------------------------------------ touch: drag to scroll, tap, double tap

        bool GalleryWantsTouch(Vector3 at)
        {
            return page == Page.Gallery && at.y < gTop && at.y > gBottom;
        }

        void GalleryTouchBegin(int h, Vector3 at, bool left)
        {
            touch[h] = new TouchInfo { Start = at, Last = at, T0 = Time.unscaledTime, Left = left };
            dragging = true;
            scrollTarget = -1f;
            scrollVel = 0f;
        }

        void GalleryTouchMove(int h, Vector3 p)
        {
            if (!touch[h].Moving)
            {
                // A poke always wobbles a little: nothing scrolls until the finger has moved TapSlop from where it touched.
                float sx = p.x - touch[h].Start.x, sy = p.y - touch[h].Start.y;
                if (sx * sx + sy * sy < TapSlop * TapSlop) return;
                touch[h].Moving = true;
                touch[h].Last = p;   // start following from here, so the content does not jump
                return;
            }

            float dy = p.y - touch[h].Last.y;
            scroll += dy / rowH;   // the content follows the finger: dragging up scrolls down
            ClampScroll();
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) touch[h].Vy = Mathf.Lerp(touch[h].Vy, dy / rowH / dt, 0.4f);
            touch[h].Last = p;
        }

        void GalleryTouchEnd(int h, Vector3 p)
        {
            dragging = touching[1 - h];
            TouchInfo t = touch[h];
            float duration = Time.unscaledTime - t.T0;
            if (t.Moving) scrollVel = Mathf.Clamp(t.Vy, -8f, 8f);   // a drag: coast on
            else if (duration < TapMaxTime) GalleryTap(t.Start, t.Left);   // stayed in the dead zone and was quick: a tap
        }

        void CancelTouch(int h)
        {
            if (!touching[h]) return;
            touching[h] = false;
            dragging = touching[0] || touching[1];
        }

        void GalleryTap(Vector2 at, bool left)
        {
            int index = TileAt(at);
            if (index < 0) return;
            phone.Haptic(left, 0.2f, 0.03f);
            if (index == lastTapIndex && Time.unscaledTime - lastTapTime < DoubleTapTime)
            {
                lastTapIndex = -1;
                OpenViewer(index);   // the second tap on the same photo opens it
                return;
            }
            selected = index;
            lastTapIndex = index;
            lastTapTime = Time.unscaledTime;
        }

        int TileAt(Vector2 at)
        {
            int col = -1;
            for (int c = 0; c < Cols; c++)
                if (Mathf.Abs(at.x - (c == 0 ? -colX : colX)) <= tileW * 0.5f) col = c;
            if (col < 0) return -1;

            float fromTop = (gTop - gap * 0.5f - at.y) / rowH + scroll;   // rows down from the top of the content
            if (fromTop < 0f) return -1;
            int row = (int)Mathf.Floor(fromTop);
            if ((fromTop - row) * rowH > tileH) return -1;                 // in the gap between rows
            int index = row * Cols + col;
            return index < photos.Count ? index : -1;
        }

        // ------------------------------------------------------------------ viewer

        void OpenViewer(int index)
        {
            viewIndex = index;
            viewW = 0;
            SetPage(Page.Viewer);
            ShowViewerPhoto();
        }

        void ShowViewerPhoto()
        {
            if (viewIndex < 0 || viewIndex >= photos.Count) { SetPage(Page.Gallery); return; }
            PhotoInfo p = photos[viewIndex];

            PhoneText.Set(vCount, (viewIndex + 1) + " / " + photos.Count);
            PhoneText.Set(vLine1, p.Time.ToString("g"));   // the PC's short date and time
            string map = PhotoLibrary.FriendlyZones(p.Zones);
            PhoneText.Set(vLine2, (p.Selfie ? "Selfie camera" : "Rear camera") + " - " + (map.Length > 0 ? map : "Unknown map"));

            viewW = 0;
            vPhoto.T.gameObject.SetActive(false);
            Thumb th;
            if (thumbs.TryGetValue(p.Path, out th)) UploadViewer(th.Rgba, th.W, th.H);   // the small picture first
            else worker.Load(p.Path, ThumbHeight, OnLoaded);
            worker.Load(p.Path, ViewHeight, OnLoaded);                                    // then the sharp one
        }

        void UploadViewer(byte[] rgba, int w, int h)
        {
            if (viewTex == null)
            {
                viewTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "GP_View", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                textures.Add(viewTex);
            }
            else if (viewTex.width != w || viewTex.height != h) viewTex.Reinitialize(w, h);
            viewTex.LoadRawTextureData(rgba);
            viewTex.Apply(false);
            PhoneMaterials.SetTexture(vPhoto.M, viewTex);
            PhoneMaterials.SetCrop(vPhoto.M, Vector2.one, Vector2.zero);
            viewW = w;
            viewH = h;
            PlaceViewerPhoto();
            vPhoto.T.gameObject.SetActive(true);
        }

        void PlaceViewerPhoto()
        {
            // Fit the whole photo in the space between the bars, keeping its shape.
            float aspect = (float)viewW / viewH;
            float aw = gSw * 0.98f, ah = vAreaH * 0.98f;
            float pw = Mathf.Min(aw, ah * aspect);
            Place(vPhoto, 0f, vAreaCy, pw, pw / aspect, -0.0008f);
        }

        void UpdateViewer()
        {
            // Dim an arrow at the end of the list.
            bool prevOk = viewIndex > 0, nextOk = viewIndex < photos.Count - 1;
            if (prevOk != lastPrevOk) { lastPrevOk = prevOk; PhoneMaterials.SetColor(prevPart.M, prevOk ? Color.white : new Color(1f, 1f, 1f, 0.3f)); }
            if (nextOk != lastNextOk) { lastNextOk = nextOk; PhoneMaterials.SetColor(nextPart.M, nextOk ? Color.white : new Color(1f, 1f, 1f, 0.3f)); }
        }

        void StepViewer(int d)
        {
            int n = Mathf.Clamp(viewIndex + d, 0, photos.Count - 1);
            if (n == viewIndex) return;
            viewIndex = n;
            ShowViewerPhoto();
        }

        void CloseViewer()
        {
            selected = viewIndex;
            EnsureVisible(selected);
            SetPage(Page.Gallery);
        }

        // ------------------------------------------------------------------ delete

        void AskDelete()
        {
            if (viewIndex < 0 || viewIndex >= photos.Count) return;
            SetPage(Page.Confirm);
        }

        void CancelDelete()
        {
            SetPage(Page.Viewer);
        }

        void DoDelete()
        {
            if (viewIndex < 0 || viewIndex >= photos.Count) { SetPage(Page.Gallery); return; }
            string path = photos[viewIndex].Path;
            worker.Delete(path, delegate (bool ok, string error) { OnDeleted(path, ok, error); });
            SetPage(Page.Viewer);
            PhoneText.Set(vLine2, "Deleting...");
        }

        void OnDeleted(string path, bool ok, string error)
        {
            if (!ok)
            {
                log.LogWarning("gallery: could not delete " + path + ": " + error);
                if (page == Page.Viewer || page == Page.Confirm) PhoneText.Set(vLine2, "Could not delete this photo");
                return;
            }

            log.LogInfo("gallery: moved to Deleted: " + path);
            int removed = photos.FindIndex(p => p.Path == path);
            thumbs.Remove(path);
            if (removed < 0) return;
            photos.RemoveAt(removed);
            for (int i = 0; i < tiles.Length; i++) tiles[i].Index = -1;

            if (selected == removed) selected = -1; else if (selected > removed) selected--;

            if (page == Page.Viewer || page == Page.Confirm)
            {
                if (photos.Count == 0) { viewIndex = -1; SetPage(Page.Gallery); }
                else
                {
                    if (viewIndex > removed) viewIndex--;
                    viewIndex = Mathf.Clamp(viewIndex, 0, photos.Count - 1);   // show the next photo (or the last one)
                    ShowViewerPhoto();
                }
            }
            else if (viewIndex > removed) viewIndex--;
        }

        // ------------------------------------------------------------------ cleanup

        void DestroyGallery()
        {
            if (worker != null) { worker.Dispose(); worker = null; }
        }
    }
}

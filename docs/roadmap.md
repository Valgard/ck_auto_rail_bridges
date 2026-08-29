# Auto Rail Bridges — Roadmap

Points that are **deliberately cut to stand alone**, not a shopping list for a
release. The useful question is "which point next?", never "what goes into
version X" — a version collects whatever happened to be finished by then.

Each entry records what is already settled and what still has to be decided, so
picking one up does not mean re-deriving the groundwork.

## Still images, and the clips on YouTube for the Steam page

This mod is documented entirely in motion — and both destinations that matter
want a still.

**Settled: there is no still image at all.** `sources/` holds one clip per
bridge type, each twice over: `ck_auto_rail_bridges_wood` and `_stone` as a GIF
(35 MB and 38 MB) and as an MP4 (under 2 MB each). Nothing in there is a frame
that can be dropped into a gallery. That is defensible for what this mod does —
a bridge building itself is a process — but it leaves the mod.io and Workshop
galleries with the logo alone, and both are filled in by hand since the publish
pipeline uploads the logo and nothing else.

**Settled: the size gap is why the GIFs are linked rather than attached.**
`CK_DISCORD_MEDIA` points at the two GIFs as `raw.githubusercontent.com` URLs,
which `utils/discord_post.py` turns into follow-up messages — the route past
Discord's upload ceiling. The MP4s exist because they are a twentieth of the
size, and they are the files a video host wants.

**Settled: Steam has no route for an uploaded video.** A Workshop item carries
a single preview image — `previewfile` is the field, and `ck-workshop` sets that
and nothing else. A video reaches the item page only as a linked YouTube video,
added through the website against a Steam account, so this half is browser work
that no part of this pipeline can do or record.

**To decide.** What the stills show: the finished bridge states the point, the
mid-build frame states the mechanism, and it is open whether wood and stone each
need their own or whether one of each kind covers it. Whether the MP4s go up as
they are or get a title card, since a YouTube page is a public surface with its
own titles and descriptions. Whether unlisted uploads can be linked on a
Workshop item at all, or whether Steam requires them public — that decides
whether these clips become a findable YouTube presence or stay accessories to
the mod page. And whether the GIFs stay the Discord route once the videos have
URLs.

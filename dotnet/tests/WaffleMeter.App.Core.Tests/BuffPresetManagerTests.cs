using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class BuffPresetManagerTests : IDisposable
{
    private readonly string _temp;
    private readonly List<HashSet<int>> _hiddenApplied = new();
    private readonly List<HashSet<int>> _voiceApplied = new();

    public BuffPresetManagerTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_presets_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>A settings instance backed by the shared temp dir — a second call re-reads the same file.</summary>
    private MeterSettings OpenSettings() => new(new PropertyHandler(_temp));

    private BuffPresetManager Manage(MeterSettings settings) =>
        new(settings, h => _hiddenApplied.Add(h), v => _voiceApplied.Add(v));

    private static BuffPresetSet Blob(MeterSettings s)
    {
        BuffPresetSet? set = BuffPresetCodec.Decode(s.BuffUiPresets);
        Assert.NotNull(set);
        return set;
    }

    [Fact]
    public void Seeds_three_slots_from_live_settings_on_first_launch()
    {
        MeterSettings s = OpenSettings();
        s.BuffUiHidden = "111,222";
        s.BuffUiVoice = "333";
        s.BuffUiIconSize = 34;
        s.BuffUiTransparent = false;

        using BuffPresetManager m = Manage(s);

        Assert.Equal(0, m.ActiveIndex);
        Assert.Equal(new[] { "프리셋 1", "프리셋 2", "프리셋 3" }, m.Names);
        BuffPresetSet blob = Blob(s);
        Assert.Equal(3, blob.Slots.Count);
        Assert.All(blob.Slots, slot =>
        {
            Assert.Equal("111,222", slot.Hidden);
            Assert.Equal("333", slot.Voice);
            Assert.Equal(34, slot.IconSize);
            Assert.False(slot.Transparent);
        });

        // Seeding must not touch the live config, nor push anything at the buff store.
        Assert.Equal("111,222", s.BuffUiHidden);
        Assert.Empty(_hiddenApplied);
        Assert.Empty(_voiceApplied);
    }

    // The end-to-end guard: a Korean name written through PropertyHandler and read back by a fresh instance.
    // Stored raw rather than Base64, every Korean char would come back as '?'.
    [Fact]
    public void Slot_name_survives_a_korean_round_trip_through_the_settings_file()
    {
        MeterSettings first = OpenSettings();
        using (BuffPresetManager m = Manage(first))
        {
            m.RenameSlot(1, "레이드 지원");
            m.RenameSlot(2, "딜링");
        }

        MeterSettings reopened = OpenSettings();
        using BuffPresetManager reloaded = Manage(reopened);

        Assert.Equal("프리셋 1", reloaded.Names[0]);
        Assert.Equal("레이드 지원", reloaded.Names[1]);
        Assert.Equal("딜링", reloaded.Names[2]);
    }

    [Fact]
    public void Rename_falls_back_to_the_default_name_when_blank()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);

        m.RenameSlot(1, "레이드");
        m.RenameSlot(1, "   ");

        Assert.Equal("프리셋 2", m.Names[1]);
    }

    [Fact]
    public void Editing_a_buff_setting_auto_saves_into_the_active_slot_only()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);
        m.SelectSlot(1);

        s.BuffUiTransparent = false;
        s.BuffUiHidden = "123";
        s.BuffTtsOnEnd = true;

        BuffPresetSet blob = Blob(s);
        Assert.Equal(1, blob.Active);
        Assert.False(blob.Slots[1].Transparent);
        Assert.Equal("123", blob.Slots[1].Hidden);
        Assert.True(blob.Slots[1].TtsOnEnd);

        // The untouched slots keep the seeded config.
        Assert.True(blob.Slots[0].Transparent);
        Assert.Equal("", blob.Slots[0].Hidden);
        Assert.True(blob.Slots[2].Transparent);
    }

    [Fact]
    public void Selecting_a_slot_applies_it_live_and_pushes_both_code_sets_to_the_store()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);

        // Give slot 1 a config of its own, then go back to slot 0 and forward again.
        m.SelectSlot(1);
        s.BuffUiHidden = "10,20";
        s.BuffUiVoice = "30";
        s.BuffUiIconSize = 34;
        s.BuffUiGrayOnCooldown = true;
        m.SelectSlot(0);
        _hiddenApplied.Clear();
        _voiceApplied.Clear();

        m.SelectSlot(1);

        Assert.Equal(1, m.ActiveIndex);
        Assert.Equal("10,20", s.BuffUiHidden);
        Assert.Equal("30", s.BuffUiVoice);
        Assert.Equal(34, s.BuffUiIconSize);
        Assert.True(s.BuffUiGrayOnCooldown);
        Assert.Equal(new HashSet<int> { 10, 20 }, Assert.Single(_hiddenApplied));
        Assert.Equal(new HashSet<int> { 30 }, Assert.Single(_voiceApplied));
    }

    // Applying a slot writes the live settings, which fires the very PropertyChanged the auto-save listens to.
    // Without the suppression guard the switch would capture the incoming config back over the slot it left.
    [Fact]
    public void Switching_away_preserves_the_previous_slot()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);
        s.BuffUiHidden = "999";
        s.BuffUiTextColor = "#112233";

        m.SelectSlot(1);

        Assert.Equal("", s.BuffUiHidden); // slot 1 was seeded before slot 0 was edited
        BuffPresetSet blob = Blob(s);
        Assert.Equal("999", blob.Slots[0].Hidden);
        Assert.Equal("#112233", blob.Slots[0].TextColor);
        Assert.Equal("", blob.Slots[1].Hidden);
    }

    [Fact]
    public void Selecting_a_slot_persists_the_blob_exactly_once()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);
        int writes = 0;
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeterSettings.BuffUiPresets))
            {
                writes++;
            }
        };

        m.SelectSlot(1);

        Assert.Equal(1, writes); // no feedback loop: the blob's own write is not captured as an edit
    }

    [Fact]
    public void Master_toggle_and_observed_catalog_are_not_part_of_a_preset()
    {
        MeterSettings s = OpenSettings();
        using BuffPresetManager m = Manage(s);
        string before = s.BuffUiPresets;

        s.ShowBuffUi = !s.ShowBuffUi;
        s.BuffUiObserved = "1,2,3";
        s.BuffUiDefaultsApplied = true;

        Assert.Equal(before, s.BuffUiPresets);
    }

    [Fact]
    public void Re_seeds_when_the_stored_blob_is_corrupt_or_the_wrong_shape()
    {
        MeterSettings s = OpenSettings();
        s.BuffUiHidden = "77";
        s.BuffUiPresets = BuffPresetCodec.Encode(new BuffPresetSet
        {
            Active = 7, // out of range, and only two slots
            Slots = [new BuffPreset { Name = "a" }, new BuffPreset { Name = "b" }],
        });

        using BuffPresetManager m = Manage(s);

        Assert.Equal(0, m.ActiveIndex);
        Assert.Equal(3, Blob(s).Slots.Count);
        Assert.Equal(new[] { "프리셋 1", "프리셋 2", "프리셋 3" }, m.Names);
        Assert.All(Blob(s).Slots, slot => Assert.Equal("77", slot.Hidden));
    }

    // A build that predates presets (or a hand-edited file) can move the live settings behind the blob's back.
    // Live wins on load, and the active slot is healed back onto it.
    [Fact]
    public void Load_heals_the_active_slot_from_the_live_settings()
    {
        MeterSettings first = OpenSettings();
        using (BuffPresetManager m = Manage(first))
        {
            m.SelectSlot(2);
            m.RenameSlot(2, "필드");
            first.BuffUiHidden = "5";
        }

        MeterSettings reopened = OpenSettings();
        reopened.BuffUiHidden = "5,6,7"; // changed without the manager watching
        _hiddenApplied.Clear(); // forget what the first manager's SelectSlot legitimately pushed
        using BuffPresetManager reloaded = Manage(reopened);

        Assert.Equal(2, reloaded.ActiveIndex);
        Assert.Equal("필드", reloaded.Names[2]);
        Assert.Equal("5,6,7", reopened.BuffUiHidden); // live untouched
        Assert.Equal("5,6,7", Blob(reopened).Slots[2].Hidden); // slot re-synced onto live
        Assert.Empty(_hiddenApplied); // load never re-applies through the store
    }
}

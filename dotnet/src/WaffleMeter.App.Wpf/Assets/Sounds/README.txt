notification.wav — alarm notification sound

Source : Pixabay sound effect "new-notification-051" (id 494246), by "universfield"
         https://pixabay.com/sound-effects/기술-new-notification-051-494246/
License: Pixabay Content License — free for commercial and non-commercial use, no attribution required.
         Used as an in-app alarm sound, embedded as a Resource (not redistributed on a standalone basis).
         https://pixabay.com/service/license-summary/

Processing: the original .mp3 was converted to 16-bit PCM WAV (44.1 kHz, stereo) with ffmpeg so it can be
played by System.Media.SoundPlayer; AlarmSound scales the 16-bit samples by the volume setting at playback
(SoundPlayer has no volume control). If this file is missing/unreadable, AlarmSound falls back to a
procedurally-synthesized glass-bell chime.

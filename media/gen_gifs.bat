@echo off
REM Loop through all MP4 files in current folder
for %%f in (*.mp4) do (
    echo Processing %%f ...
    
    REM Generate palette
    ffmpeg -y -i "%%f" -vf "fps=10,scale=320:-1:flags=lanczos,palettegen" "%%~nf_palette.png"
    
    REM Create GIF using palette
    ffmpeg -y -i "%%f" -i "%%~nf_palette.png" -filter_complex "fps=10,scale=320:-1:flags=lanczos[x];[x][1:v]paletteuse" "%%~nf.gif"
    
    REM Optional: Delete temporary palette
    del "%%~nf_palette.png"
)
echo All done!
pause

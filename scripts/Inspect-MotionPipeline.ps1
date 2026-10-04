[CmdletBinding()]
param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI')

# Reproducible, targeted inspection. Exports inferred C/assembly only to artifacts.
# Some methods exist in only one game; matching a name is not hook validation.
$pattern = '^(CameraFollowing\$\$(UpdateController|LibraryUpdateController)|Last\.Systems\.Camera\.CameraController\$\$SetPosition|Last\.Map\.Renderer\.BaseMapRenderer\$\$(UpdateMapScrollIfNeed|LibraryUpdateMapScrollIfNeed|UpdateLayerOffsetBase|UpdateMapScroll)|Last\.Entity\.Field\.Field(Entity|SpriteEntity|CharaEntity|Player|ScrollDummyEntity)\$\$(UpdateEntity|UpdateMovingSetPosition|UpdateMoveFinishedSetPosition|LateUpdateEntity|LateUpdatePositionToMaterial|GetEntityPosition|GetEntityOffset|SquareMoveFinished|OnMoveCharacter)|Last\.Map\.FieldController\$\$(UpdateController|LateUpdateController|UpdateCamera|UpdateVisualInstancePosition|ChangeCameraTarget|SetCameraOffset|UpdateMapScrollOffset|SetMapScrollOffset)|Last\.Map\.MapMultipleScroll\$\$(UpdateScroll|UpateAutoScroll)|Last\.Map\.EventActionScript\$\$ChangeCameraTargetToScrollDummy)$'
& "$PSScriptRoot/Export-NativeMethods.ps1" -Game $Game -MethodPattern $pattern

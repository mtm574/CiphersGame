# Course hit feedback

BasicScene has a CourseHitController on RotatingArenaCenter and CourseObstacle
markers on its 20 obstacle groups. Torus/floor geometry is excluded. Disable a
CourseObstacle marker to exclude that group. Do not put one on the arena root.

At runtime the controller creates 14 kinematic trigger capsules following the
solved avatar bones, plus an opaque red overlay attached to the VR camera.
The head proxy follows the headset rather than the head bone's scale. No new
colliders or forces are applied to the avatar or XR Origin.

On a new obstacle contact, the flash holds at alpha 1 for 0.1 seconds and fades
out over 0.1 seconds. The course pauses for 2 seconds while head/hand tracking
continues. Jogging rests. Windmill and sliding-obstacle phases and the arena's
initial delay freeze; they resume where they stopped. Global time is unchanged.

Several body parts hitting one obstacle count once. Contacts during a pause do
not extend it. Each contacted obstacle rearms only after all contact pairs have
cleared for 0.1 seconds. Resume is automatic even if the player is still touching
an obstacle; there is no knockback, scoring, sound, or positional reset.

Adjust Pause Seconds, Rearm Seconds, and Hitbox Sizes on CourseHitController.
Sizes are radii in metres at avatar scale 1. The generated hitboxes can be
inspected in Play mode under Player Hitboxes (runtime). Shapes approximate the
body; they are not per-triangle mesh collisions.

For new movers, assign the controller to their Course property and skip movement
while Course.IsPaused. Advance any motion clock only while movement is running.
New obstacles need a CourseObstacle marker on their logical root and colliders
on the actual hazard geometry. Leave the markers off scenery and the floor.

Run Tools > VR Body > Validate Course Hits outside Play mode. Results are saved
to Library/CourseHitValidation/results.txt. Validation uses a private scene.
Headset testing is still needed for both-eye flash coverage, tuned hitbox sizes,
and contacts with moving blades at the course's actual operating speeds.

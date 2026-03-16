Sprite Editor rework

The current sprite editor has an inspector which we do not need ot change and also an editor at the bottom which is the dopesheet and a toolbar.  This has all become quite broken and I would like to clean it up.  What I want is the bottom bar to be more like figma and framer so its a floating bar centered on the screen.  In both figma and framer it is even on top of the inspector if you size the window too small.  I included a picture of this bar for reference.  

This plan is multi part.  The first part I want us to focus on updating the SpriteEditor.pen file with the new layout and buttons.  Then once we are settled on that we can plan out the code changes.   

Lets explore what there is to put on the this toolbar

* Toggle for "tiled mode"
* Toggle to show / hide the dope sheet (animated)
* Toggle to show / hide the generated image
* Button for different tools ?  Pen, knife, rectangle, circle, etc,
* Dope sheet if animation enabled (maybe the toolbar grows vertically and it just runs under it)

We used to have a more on there but a lot was moved to the inspector such as colors, operation, etc which is good.   So the toolbar is more for toggles and tool and navigation.  Consider the dope sheet navigation of sorts to navigate between frames.

Lets start there, think it through deeply, check out the code and the current .pen file, and come up with a plan to modify the .pen.
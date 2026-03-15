Sprite Refactor

* Combine texture and sprite concepts in the editor
* Remove TextureDocument and TextureEditor
* Image file formats such as png are a SpriteDocument now but are flagged as static images.  This means the vector editor is disabled and the image generation is disabled.
* Support multiple image formats for sprites (png, tga, jpg, etc, using library)
* if the <name>.sprite exists then it is a standard sprite.   If a <name>.[png|jpg|etc] exists then it is a static image.  If both exist then it is a generated image (we remove the image data from .sprite and save as a read file like .webp or .png along side the sprite.     
* Sprites can have up to 3 files.    .sprite,  .meta, and .[png|jpg|etc]   
* Sprite constraits still work for static images if enabled and will clip or expand the sprite. 
* we will need to add support into the editor to properly hanlde the extenions to route to the correct document format, so a more complex extensions resolver.
* There is no longer a maximum limit on sprite size, but there is a limit on the size a sprite can be to be included in the atlas.  If a sprite does not fit in an atlas than its content will be part of the sprite export.  This means the sprite asset will need to internally manage a texture in this case and know it is not in the atlas.  
* Simplify sprite bone and layer associations so they are on the sprite level now.  This means any given sprite can be set to a single layer and a single bone.   
* Simplify sprites so they are no longer multi-layer during export, they always result in a single image that is either in the atlas or in its own texture if too big.

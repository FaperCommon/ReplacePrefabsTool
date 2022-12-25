# ReplacePrefabsTool (RPT)
RPT tool to replace prefabs with all references to/within them in the scene. RPT is an add-on for [Moolt/SmartReplace](https://github.com/Moolt/SmartReplace)

# Setup
Need to download [unitypackage](https://github.com/FaperCommon/ReplacePrefabsTool/blob/main/ReplacePrefabsTool.unitypackage) and import into target project. 
Well done!

# Workflow
After importing into your project, it is possible to open the replace prefab window via `Tools -> Replace Prefabs...`

# Cases
- When it is necessary to replace a lot of prefabs on the stage, while maintaining all references to prefabs and inside prefabs;
- When you need to replace prefabs for many scenes.

RPT was written and used for a hyper-casual multi-level game, where it was necessary to replace one type of resource on the scene with another and at the same time not lose refences to these prefabs inside the scene and references inside the resources to scene objects. 
Sometimes the prefabs had different sizes. 
The number of resources on the stage reached up to a couple of dozen. 
The number of levels in the game reached 1000+.


# Usage / Functions 
The use of tools is simplified to the primitive.

In the `Replace prefab` window, you need to set Original prefab (replaced) and New prefab (what we are replacing), set the offset relative to the old prefab (optional, if not necessary, leave (0, 0, 0)). 

After installing the prefabs in the window - click on the Replace button and RPT to replace all the original prefabs on the scene with new ones. In case of error - there is support for Ctrl + Z, Ctrl + Y. 


# Differences with SmartReplace
- Added the ability to replace multiple objects on the scene;
- Added the ability to set a position offset relative to the originl prefab (Prefabs do not always have the same size, and setting many objects by hand is a long practice);
- Support Ctrl+Z, Ctrl+Y
- Work with prefabs, not with scene objects, which allows you to replace not one, but all objects on the stage and switch scenes without closing the window, making work easier.


# Contact
You can contact me anytime at kadnikovfap@gmail.com.

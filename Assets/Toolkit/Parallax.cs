using UnityEngine;
using Rhinotap;

namespace Rhinotap.Toolkit
{
    public class Parallax : MonoBehaviour
    {
        #region Singleton Instance
        private static Parallax _instance; //instance of this object
        public static Parallax instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindAnyObjectByType<Parallax>();//find the instance in the scene
                    if (_instance == null)
                        Debug.LogError("Mobile Input Manager Not available in the scene");
                }
                return _instance;
            }
        }
        #endregion

        #region Static Api
        public static Camera GetCamera { get { return instance.parallaxCamera; } }
        public static Transform HiddenFolder { get { return instance.hiddenFolder; } }

        public static string SortingLayerBackground { get { return instance.layerBackgroundItems; } }
        public static string SortingLayerForeground { get { return instance.layerForegroundItems; } }

        public static void EnableLayer(string OriginalGameObjectName)
        {
            bool found = false;
            for(int i = 0; i < instance.items.Length; i++)
            {
                if( instance.items[i].itemName == OriginalGameObjectName)
                {
                    instance.items[i].Enable();
                    found = true;
                    break;
                }
            }
            if (!found)
                Debug.Log("Rhinotap Parallax Manager: Could not enable Parallax Layer: " + OriginalGameObjectName + ". Object could not be found.");
        }

        public static void DisableLayer(string OriginalGameObjectName)
        {
            bool found = false;
            for (int i = 0; i < instance.items.Length; i++)
            {
                if (instance.items[i].itemName == OriginalGameObjectName)
                {
                    instance.items[i].Disable();
                    found = true;
                    break;
                }
            }
            if (!found)
                Debug.Log("Rhinotap Parallax Manager: Could not disable Parallax Layer: " + OriginalGameObjectName + ". Object could not be found.");
        }

        public static void Pause()
        {
            instance.isPaused = true;
        }
        public static void Resume()
        {
            instance.isPaused = false;
        }
        public static void Toggle()
        {
            instance.isPaused = !instance.isPaused;
        }
        #endregion

        #region Inspector Params
        //===========| INSPECTOR |==============//

        //Main camera to apply to
        [Header("Camera object to apply parallax")]
        [Space(10)]
        [SerializeField]
        private Camera parallaxCamera;
        


        [Header("Sorting Layer Names")]
        [Space(10)]

        [Tooltip("Items that are distance 0, 100 will be moved to this layer")]
        [SerializeField]
        private string layerBackgroundItems = "ParallaxBackground";

        [Tooltip("Items that are distance -1, -100 will be moved to this layer")]
        [SerializeField]
        private string layerForegroundItems = "ParallaxForeground";


        //Array of parallaxitems
        [Header("Parallax Items")]
        [Space(10)]
        [SerializeField]
        private ParallaxItem[] items;

        [Header("Level Backgrounds")]
        [Space(10)]
        [SerializeField]
        private Sprite oceanBackgroundSprite;
        [SerializeField]
        private Sprite lakeBackgroundSprite;

        #endregion

        #region internal variables
        //===========| INTERNAL VARS |==============//
        private Vector2 camStartPos;
        private Vector2 deltaMovement;
        private bool isPaused = false;
        private bool hasError = false;

        //Track camera scale
        private float initialCamSize = 1f;
        private float currentCamSize = 1f;
        private float currentCamScale = 1f;

        //Empty game object to hold hidden originals
        private Transform hiddenFolder;

        private static Sprite cachedLakeSprite;
        private static Sprite cachedOceanSprite;
        #endregion

        #region Background Management
        public static void ClearCache()
        {
            cachedLakeSprite = null;
            cachedOceanSprite = null;
        }

        private static Sprite LoadSpriteAsset(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(relativePath);
            if (assets != null && assets.Length > 0)
            {
                Sprite best = null;
                float maxArea = 0f;
                foreach (var a in assets)
                {
                    if (a is Sprite s && s != null)
                    {
                        float area = s.rect.width * s.rect.height;
                        if (area > maxArea)
                        {
                            maxArea = area;
                            best = s;
                        }
                    }
                }
                if (best != null) return best;
            }

            Sprite direct = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(relativePath);
            if (direct != null) return direct;
#endif
            string fn = System.IO.Path.GetFileNameWithoutExtension(relativePath);
            Sprite res = Resources.Load<Sprite>(fn);
            if (res != null) return res;

            Sprite[] all = Resources.FindObjectsOfTypeAll<Sprite>();
            if (all != null && all.Length > 0)
            {
                Sprite best = null;
                float maxArea = 0f;
                foreach (var s in all)
                {
                    if (s != null && s.name.Contains(fn))
                    {
                        float area = s.rect.width * s.rect.height;
                        if (area > maxArea)
                        {
                            maxArea = area;
                            best = s;
                        }
                    }
                }
                if (best != null) return best;
            }

            try
            {
                string fullPath = relativePath.StartsWith("Assets")
                    ? System.IO.Path.Combine(Application.dataPath, relativePath.Substring("Assets/".Length))
                    : relativePath;
                if (System.IO.File.Exists(fullPath))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 31.311f);
                    }
                }
            }
            catch { }

            return null;
        }

        public Sprite GetLakeSprite()
        {
            if (lakeBackgroundSprite != null) return lakeBackgroundSprite;
            if (cachedLakeSprite != null) return cachedLakeSprite;

            cachedLakeSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/Lost_Lake_BG.png");
            if (cachedLakeSprite == null) cachedLakeSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/river_background.png");
            if (cachedLakeSprite == null) cachedLakeSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/Game_bg_lake.png");

            return cachedLakeSprite;
        }

        public Sprite GetOceanSprite()
        {
            if (oceanBackgroundSprite != null) return oceanBackgroundSprite;
            if (cachedOceanSprite != null) return cachedOceanSprite;

            cachedOceanSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/Coral_Coast_BG.png");
            if (cachedOceanSprite == null) cachedOceanSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/ocean_background.png");
            if (cachedOceanSprite == null) cachedOceanSprite = LoadSpriteAsset("Assets/Graphics/Backgrounds/Game_bg_ocean.png");
            if (cachedOceanSprite == null) cachedOceanSprite = Resources.Load<Sprite>("game_bg");

            return cachedOceanSprite;
        }

        public void ApplyLevelBackground()
        {
            ClearCache();

            bool isLake = LevelManager.IsCurrentLakeLevel;

            if (items != null && items.Length > 0)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null || items[i].Item == null) continue;
                    string itemName = items[i].Item.name.ToLower();

                    // Skip particle systems
                    if (itemName.Contains("particle")) continue;

                    if (itemName.Contains("bg") || itemName.Contains("pixelated") || itemName.Contains("background") || i == 0)
                    {
                        var sr = items[i].Item.GetComponent<SpriteRenderer>();

                        Sprite targetSprite = isLake ? GetLakeSprite() : GetOceanSprite();
                        if (targetSprite != null)
                        {
                            if (sr != null)
                            {
                                sr.sprite = targetSprite;
                            }
                            items[i].UpdateSprite(targetSprite);
                            Debug.Log($"[Parallax] Applied {(isLake ? "Lake" : "Ocean")} background sprite ('{targetSprite.name}') for Level {LevelManager.CurrentLevel}");
                        }
                        else
                        {
                            Debug.LogWarning($"[Parallax] Could not load {(isLake ? "Lake" : "Ocean")} background sprite for Level {LevelManager.CurrentLevel}");
                        }
                    }
                }
            }

            // Remove/hide ocean reef elements from river levels and enable river elements
            SetOceanReefElementsActive(!isLake);
            SetRiverElementsActive(isLake);

            // Configure ambient background bubble particle flows (Ocean: Right-to-Left horizontal flow; River: vertical flow)
            ConfigureBackgroundBubbles(isLake);

            // Refresh horizontal ocean ambient bubbles
            OceanBackgroundBubbles.Refresh();
        }

        public void ConfigureBackgroundBubbles(bool isLake)
        {
            ParticleSystem[] allBubbles = GetComponentsInChildren<ParticleSystem>(true);
            if (allBubbles == null || allBubbles.Length == 0) return;

            foreach (var ps in allBubbles)
            {
                if (ps == null) continue;
                if (!ps.name.Contains("bubbleParticles") && !ps.name.Contains("bgParticles")) continue;

                var main = ps.main;
                var shape = ps.shape;
                var emission = ps.emission;

                // Subtle emission rate and smaller delicate bubble size (0.05 - 0.10) to keep the screen crisp and uncluttered
                emission.enabled = true;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(3.5f);

                if (!isLake)
                {
                    // === Ocean Levels: Horizontal Flow (Right to Left) ===
                    ps.transform.localEulerAngles = new Vector3(0f, -90f, 0f);

                    shape.shapeType = ParticleSystemShapeType.Rectangle;
                    shape.scale = new Vector3(2.0f, 24.0f, 0.0f);
                    shape.position = new Vector3(0f, 0f, -6.5f); // Spawns along the right side of the screen
                    shape.rotation = Vector3.zero;

                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(14.0f);
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                }
                else
                {
                    // === River / Lake Levels: Vertical Flow (Bottom to Top) ===
                    ps.transform.localEulerAngles = new Vector3(90f, 0f, 0f);

                    shape.shapeType = ParticleSystemShapeType.Rectangle;
                    shape.scale = new Vector3(12.96f, 4.13f, 0.0f);
                    shape.position = new Vector3(0f, 0f, -1.56f);
                    shape.rotation = new Vector3(270f, 0f, 0f);

                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(10.0f);
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                }

                if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                {
                    ps.Play();
                }
            }
        }

        public static void SetOceanReefElementsActive(bool active)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isLoaded)
            {
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root.name.Contains("OceanEnvironment"))
                    {
                        // Extra procedural rocks removed; ocean strictly uses Ocean_reef_element_1, Ocean_reef_element_3 and Clam
                        root.SetActive(false);
                    }
                    else if (root.name.Contains("OceanReef") || root.name.StartsWith("Reef_") || root.name.Contains("Clam") || root.name.StartsWith("Ocean_") || root.name.Contains("Coral_Coast_element"))
                    {
                        root.SetActive(active);
                    }
                }
            }
        }

        public static void SetRiverElementsActive(bool active)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isLoaded)
            {
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        if (t.name.Contains("RiverEnvironment") || t.name.Contains("RiverElement") || t.name.StartsWith("River_") || t.name.Equals("River_Element1"))
                        {
                            // Elements in the river removed per user request (clean river background only)
                            t.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        public static void RefreshBackground()
        {
            if (_instance != null)
            {
                _instance.ApplyLevelBackground();
            }
        }
        #endregion

        #region Mono Behaviour
        //===========| MONO BEHAVIOUR |==============//
        private void Awake()
        {
            //Save starting positions
            if (parallaxCamera == null)
            {
                // Try to auto-assign main camera
                parallaxCamera = Camera.main;
                if (parallaxCamera == null)
                {
                    // fallback: find any Camera in scene
                    Camera anyCam = GameObject.FindAnyObjectByType<Camera>();
                    if (anyCam != null)
                        parallaxCamera = anyCam;
                }

                if (parallaxCamera == null)
                {
                    Debug.LogWarning("Rhinotap Parallax Manager: Camera reference is missing on Parallax component and no camera was found in scene. Parallax will be disabled.");
                    hasError = true;
                    return;
                }
                else
                {
                    Debug.Log("Rhinotap Parallax Manager: parallaxCamera was auto-assigned to '" + parallaxCamera.name + "'.");
                }
            }
            camStartPos = parallaxCamera.transform.position;
            deltaMovement = Vector2.zero;

            //Holder for hidden objects
            GameObject temp = new GameObject();
            temp.name = "_Original";
            hiddenFolder = temp.transform;
            hiddenFolder.position = Vector2.zero;
            hiddenFolder.localPosition = Vector2.zero;
            hiddenFolder.SetParent(transform);
            hiddenFolder.gameObject.SetActive(false);
            
            if( SortingLayer.NameToID(layerBackgroundItems) == 0)
            {
                Debug.LogError("Rhinotap Parallax Manager: Sorting Layer does not exist: \"" + layerBackgroundItems + "\"");
                layerBackgroundItems = "Default";
            }
            if (SortingLayer.NameToID(layerForegroundItems) == 0)
            {
                Debug.LogError("Rhinotap Parallax Manager: Sorting Layer does not exist: \"" + layerForegroundItems + "\"");
                layerBackgroundItems = "Default";
            }

            //Initialize each parallax item
            int i = 0;
            foreach (ParallaxItem item in items)
            {
                item.Initialize(camStartPos, i);
                i++;
            }

            // Auto-attach OceanBackgroundBubbles to ambient ocean bubble particles if present
            var bubbleObj = GameObject.Find("bubbleParticles");
            if (bubbleObj != null && bubbleObj.GetComponent<OceanBackgroundBubbles>() == null)
            {
                bubbleObj.AddComponent<OceanBackgroundBubbles>();
            }

            ApplyLevelBackground();
        }

        private void Start()
        {
            if (hasError) return;

            //Move the whole parallax system on camera
            transform.SetParent(parallaxCamera.transform, true);

            //Get cam size if ortho
            if( parallaxCamera != null && parallaxCamera.orthographic == true)
            {
                initialCamSize = parallaxCamera.orthographicSize;
                currentCamSize = parallaxCamera.orthographicSize;
            }
        }

        private void Update()
        {
            if (hasError) return;

            TrackCameraScale();

            deltaMovement = (Vector2)parallaxCamera.transform.position - camStartPos;
            if( !isPaused)
            {
                foreach (ParallaxItem item in items)
                {
                    item.Tick(deltaMovement);
                }
            }
            


        }
        #endregion

        //Scale the parallax parent object according to camera size
        void TrackCameraScale()
        {
        if (parallaxCamera == null) return;
            if (parallaxCamera.orthographic == false) return;
            currentCamSize = parallaxCamera.orthographicSize;

            if( currentCamSize != initialCamSize)
                currentCamScale = currentCamSize / initialCamSize;

            //Scale the game object
            if( currentCamScale != 1f && transform.localScale.x != currentCamScale)
            {
                transform.localScale = new Vector3(currentCamScale, currentCamScale, 1f);
            }
        }

    }

    #region ParallaxItem serializable class
    [System.Serializable]
    public class ParallaxItem
    {
        #region Settings via inspector

        //The game object that has a sprite. It will be parallaxed. It can contain childs / Animations.
        [SerializeField]
        [Header("Game Object with SpriteRenderer")]
        GameObject item;

        public GameObject Item => item;

        public void UpdateSprite(Sprite newSprite)
        {
            if (newSprite == null) return;
            if (sprite != null)
            {
                sprite.sprite = newSprite;
                width = sprite.bounds.size.x;
                height = sprite.bounds.size.y;
            }
            else if (item != null)
            {
                var sr = item.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = newSprite;
                    width = sr.bounds.size.x;
                    height = sr.bounds.size.y;
                }
            }

            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] != null)
                    {
                        var sr = items[i].GetComponent<SpriteRenderer>();
                        if (sr != null)
                        {
                            sr.sprite = newSprite;
                        }
                    }
                }
            }
        }

        //Distance from the player
        [Header("Distance from player")]
        
        [Tooltip("100: Far away, -100: very close to camera, 0: moves along the camera")]
        [SerializeField]
        [Range(-100, 100)]
        int distance = 100;

        [Header("Repeating Options")]
        

        [SerializeField]
        bool RepeatY = true;
        
        [SerializeField]
        bool RepeatX = true;
        #endregion

        #region Internal Vars
        //Grid Holder
        private GameObject[] items;

        //All grid objects will be a child of this parent. This parent will be a child of parallax. Parallax will be child of camera
        private GameObject parent;

        //Components
        private SpriteRenderer sprite;

        //Trackers
        private Vector2 deltaCameraMovement = Vector2.zero;
        private Vector2 imgOriginalPos = Vector2.zero;
        private Vector2 parentOriginalLocalPosition = Vector2.zero;
        private float width = 0f;
        private float height = 0f;

        //For naming the game object. Will use array index from Parallax 
        private int parallaxNumber = 0;
        //Name of this item via original game object name
        public string itemName { get; private set; } = "default";

        //Repeating Trackers
        private float deltaX = 0f;
        private float deltaY = 0f;
        private int deltaWidthCount = 0;
        private int deltaHeightCount = 0;

        //Parallaxing effect for this distance (Will be calculated in the engine)
        private float parallaxScale = 1f;

        //Will turn true if everything is in order
        private bool initialized = false;

        //enable/disable this layer toggle
        private bool isActive = true;
        #endregion

        #region Public API to control each parallax layer through Parallax main class
        //========================| Public API |====================//

        //Run @ awake to save initial positions and required setup
        public void Initialize(Vector2 cameraStartingPoisiton, int parallaxNo = 0)
        {
            if (item == null)
            {
                Debug.LogError("Rhinotap Parallax Manager: Parallax Item " + parallaxNo + " is not set in the inspector. Parallaxing will not work");
                return;
            }
            sprite = item.GetComponent<SpriteRenderer>();
            if (sprite == null)
            {
                Debug.LogError("Rhinotap Parallax Manager: Parallax Item " + parallaxNo + " does not have sprite renderer. Parallaxing will not work");
                return;
            }

            
            parallaxNumber = parallaxNo;
            itemName = item.name;

            //Save item position
            imgOriginalPos = item.transform.position;

            //Save dimensions
            width = sprite.bounds.size.x;
            height = sprite.bounds.size.y;

            //Set the sorting layers according to distance
            SetSortingLayer();

            //Var Validator
            if (distance < -100)
                distance = -100;
            else if (distance > 100)
                distance = 100;

            //create 9 image grid
            CreateGrid();

            parallaxScale = CalculateParallaxScale(distance);


            initialized = true;
        }

        //Tick every update
        public void Tick(Vector2 delta)
        {
            if (!initialized)
                return;
            if (!isActive)
                return;
            //Parallax manager will tell current delta movement
            deltaCameraMovement = delta;

            Vector2 scaledPosition = deltaCameraMovement * parallaxScale;

            Vector2 newLocalPosition = parentOriginalLocalPosition + scaledPosition;

            deltaX = newLocalPosition.x - parentOriginalLocalPosition.x;
            deltaY = newLocalPosition.y - parentOriginalLocalPosition.y;

            CalculateParallaxRepeatCounts();

            //Horizontal Parallax repeating
            if (RepeatX)
            {
                if (deltaWidthCount > 0) //Camera going left and item is going right in local space
                    newLocalPosition.x = newLocalPosition.x - (width * deltaWidthCount);
                else if (deltaWidthCount < 0)
                    newLocalPosition.x = newLocalPosition.x + (width * Mathf.Abs(deltaWidthCount));
            }


            ///Vertical parallax repeating
            if (RepeatY)
            {
                if (deltaHeightCount > 0)
                    newLocalPosition.y = newLocalPosition.y - (height * deltaHeightCount);
                else if (deltaHeightCount < 0)
                    newLocalPosition.y = newLocalPosition.y + (height * Mathf.Abs(deltaHeightCount));
            }



            parent.transform.localPosition = newLocalPosition;

        }

        public void Disable()
        {
            isActive = false;
            if (parent != null) parent.SetActive(false);
            if (item != null) item.SetActive(false);
        }
        public void Enable()
        {
            isActive = true;
            if (parent != null) parent.SetActive(true);
            if (item != null) item.SetActive(true);
        }
        #endregion

        //========================| Parallax Engine |====================//
        #region Parallax Engine (internal calculations)

        //Set sprite sorting layers according to distance
        private void SetSortingLayer()
        {
            if (sprite == null)
                return;

            string sortLayer = "Default";
            int sortOrder = 0;
            int originalSortingOrder = 0;

            //Determine sorting order layer name
            if( distance >= 0)
            {
                sortLayer = Parallax.SortingLayerBackground;
            }else
            {
                sortLayer = Parallax.SortingLayerForeground;
            }

            //Determine sorting order
            if( distance > 0)
            {
                sortOrder = -distance;
            }else if( distance < 0)
            {
                sortOrder = Mathf.Abs(distance);
            }
            originalSortingOrder = sprite.sortingOrder;

            sprite.sortingLayerName = sortLayer;
            sprite.sortingOrder = sortOrder;


            //Apply sprite sorting layer for childs if there are any sprites. Respect original intent.
            SpriteRenderer[] childSprites = item.GetComponentsInChildren<SpriteRenderer>(false);
            
            if( childSprites.Length > 0)
            {
                foreach( SpriteRenderer childSprite in childSprites)
                {
                    //Skip parent obj
                    if (childSprite.gameObject == item)
                        continue;

                    childSprite.sortingLayerName = sortLayer;
                    //Original difference 
                    int difference = Mathf.Abs(originalSortingOrder - childSprite.sortingOrder);
                    
                    if ( childSprite.sortingOrder < originalSortingOrder)
                    {
                        //it was in the back of the original
                        childSprite.sortingOrder = sortOrder - difference;
                    }
                    else if( childSprite.sortingOrder > originalSortingOrder)
                    {
                        childSprite.sortingOrder = sortOrder + difference;
                    }else
                    {
                        childSprite.sortingOrder = sortOrder;
                    }
                }
            }
        }

        //Create the 9 image grid and hide the original
        private void CreateGrid()
        {
            //Object to clone from
            GameObject temp = GameObject.Instantiate(item, imgOriginalPos, Quaternion.identity);

            //Create a parent
            parent = new GameObject();
            parent.transform.SetParent(Parallax.instance.transform);
            parent.name = "Parallax Item " + parallaxNumber.ToString();

            //Create a grid
            items = new GameObject[9];
            items[0] = InstantiateItem(temp, parent, "TL");
            items[1] = InstantiateItem(temp, parent, "TM");
            items[2] = InstantiateItem(temp, parent, "TR");
            items[3] = InstantiateItem(temp, parent, "ML");
            items[4] = InstantiateItem(temp, parent, "MM");
            items[5] = InstantiateItem(temp, parent, "MR");
            items[6] = InstantiateItem(temp, parent, "BL");
            items[7] = InstantiateItem(temp, parent, "BM");
            items[8] = InstantiateItem(temp, parent, "BR");

            //Trash
            GameObject.Destroy(temp);


            //item.transform.SetParent(Parallax.HiddenFolder.transform);
            item.transform.SetParent(Parallax.HiddenFolder);
            item.SetActive(false);

            parentOriginalLocalPosition = parent.transform.localPosition;

            ManageRepeatToggle();
        }

        //Create a single image for this parallax at the given position.
        //[TopLeft, TopMiddle, TopRight, MiddleLeft, MiddleMiddle, MiddleRight, BottomLeft, BottomMiddle, BottomRight] (Only capital letters)
        private GameObject InstantiateItem(GameObject original, GameObject parent, string gridPosition = "TL")
        {
            GameObject thisItem = GameObject.Instantiate(original, parent.transform);
            thisItem.name = parent.name + " [" + gridPosition + "]";
            Vector3 position = new Vector3(0, 0, 0);
            switch (gridPosition)
            {
                //TOP LEFT 
                case "TL":
                    position.x = original.transform.localPosition.x - width;
                    position.y = original.transform.localPosition.y + height;
                    break;
                //TOP MIDDLE
                case "TM":
                    position.x = original.transform.localPosition.x;
                    position.y = original.transform.localPosition.y + height;
                    break;
                //TOP RIGHT
                case "TR":
                    position.x = original.transform.localPosition.x + width;
                    position.y = original.transform.localPosition.y + height;
                    break;
                //MIDDLE LEFT
                case "ML":
                    position.x = original.transform.localPosition.x - width;
                    position.y = original.transform.localPosition.y;
                    break;
                //MIDDLE CENTER
                case "MM":
                    position.x = original.transform.localPosition.x;
                    position.y = original.transform.localPosition.y;
                    break;
                //MIDDLE RIGHT
                case "MR":
                    position.x = original.transform.localPosition.x + width;
                    position.y = original.transform.localPosition.y;
                    break;
                //BOTTOM LEFT
                case "BL":
                    position.x = original.transform.localPosition.x - width;
                    position.y = original.transform.localPosition.y - height;
                    break;
                // BOTTOM MIDDLE
                case "BM":
                    position.x = original.transform.localPosition.x;
                    position.y = original.transform.localPosition.y - height;
                    break;
                // BOTTOM RIGHT
                case "BR":
                    position.x = original.transform.localPosition.x + width;
                    position.y = original.transform.localPosition.y - height;
                    break;
            }
            thisItem.transform.localPosition = position;
            return thisItem;

        }

        //Calculate distance to parallax effect for this item
        private float CalculateParallaxScale(int distanceToCamera)
        {
            /* Parallax Calculation by given distance
             * if distance is 100, image wont move at all in the world. But it will move at the same rate as the camera in the opposite direction
             * distance = 0: image will always move with the camera. Position wont change in screen space.
             * distance = -100 image will move 2 times faster than the camera in the opposite direction
             */
            float scale;

            if (distanceToCamera == 100)
            {
                scale = 0f;
            }
            else if (distanceToCamera > 0 && distanceToCamera < 100)
            {
                scale = (float)(100 - distanceToCamera) / 100f;
                scale *= -1f;
            }
            else if (distanceToCamera == 0)
            {
                scale = 1f;
                scale *= -1f;
            }
            else
            {
                scale = (float)(100 + Mathf.Abs(distanceToCamera)) / 100f;
                scale *= -1f;
            }


            return scale;
        }

        //Disable the tiles that arent repeating
        private void ManageRepeatToggle()
        {
            if (RepeatX == false)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    //These items will stay active only
                    if (i == 1 || i == 4 || i == 7)
                    {
                        continue;
                    }
                    else
                    {
                        items[i].SetActive(false);
                    }
                }
            }

            if (RepeatY == false)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    //These items will stay active only
                    if (i == 3 || i == 4 || i == 5)
                    {
                        continue;
                    }
                    else
                    {
                        items[i].SetActive(false);
                    }
                }
            }

        }

        //How many times do each axis needs to repeat
        private void CalculateParallaxRepeatCounts()
        {

            if (Mathf.Abs(deltaX) > width)
            {
                int horizontalCount = (int)Mathf.Floor(Mathf.Abs(deltaX) / width);
                if (deltaX < 0)
                    deltaWidthCount = -horizontalCount;
                else
                    deltaWidthCount = horizontalCount;
            }
            else
            {
                deltaWidthCount = 0;
            }

            if (Mathf.Abs(deltaY) > height)
            {
                int verticalCount = (int)Mathf.Floor(Mathf.Abs(deltaY) / height);
                if (deltaY < 0)
                    deltaHeightCount = -verticalCount;
                else
                    deltaHeightCount = verticalCount;
            }
            else
            {
                deltaHeightCount = 0;
            }

            //Debug.Log("Current DeltaY: " + deltaY.ToString("n2") + " And current repeatY: " + deltaHeightCount.ToString());
            //Debug.Log("Current DeltaX: " + deltaX.ToString("n2") + " And current repeatX: " + deltaWidthCount.ToString());
        }

        #endregion
    }
    #endregion


}
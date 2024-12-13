using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using JetBrains.Annotations;
using UnityEngine.UI;
using Unity.Jobs;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System;
using System.Net;

public class IslandGenerator : MonoBehaviour
{
    public bool variableMode;

    public int size;
    [Range(0.05f, 0.5f)] public float scale;
    public float multiplier;

    public GameObject cube;
    public float cubeScale;

    string webMatrixString;
    float[,] heights;

    public int xOffset;
    public int yOffset;

    Renderer renderer;
    Texture2D noiseTexture;
    public Gradient colorGradient;
    public Material material;
    [ColorUsage(true, true)]
    public Color outlineColor;

    GameObject selectedIsland;
    List<List<Vector2Int>> detectedIslands = new();
    List<float> averageIslandHeights = new();
    List<GameObject> islands = new();

    List<Vector3> verts;
    List<Vector2> uvs;

    Vector3 blockPos;
    float gridZ;

    public int tries = 3;
    Vector3 mousePos;

    public Image gradientImage;

    public TextMeshProUGUI[] heightTexts;
    public TextMeshProUGUI avgHeightText;
    public TextMeshProUGUI triesLeftText;
    public TextMeshProUGUI endText;
    public TextMeshProUGUI endTriesLeftText;
    public TextMeshProUGUI handleText;

    public GameObject slider;
    public GameObject confirmBtn;
    public GameObject randomizeBtn;
    public GameObject endPanel;

    public GraphicRaycaster graphicRaycaster;
    public EventSystem eventSystem;

    void Start()
    {
        if (!variableMode)
        {
            slider.SetActive(false);
            size = 30;
            StartCoroutine(GetRequest("https://jobfair.nordeus.com/jf24-fullstack-challenge/test"));
        }
        else
            RandomizeOffset();

        ApplyGradient();
        endPanel.SetActive(false);
        confirmBtn.SetActive(false);
    }

    void ApplyGradient()
    {
        Texture2D gradientTexture = new(1, 256);
        gradientTexture.wrapMode = TextureWrapMode.Clamp;

        for (int i = 0; i < 256; i++)
        {
            float t = (float)i / (255);
            Color color = colorGradient.Evaluate(t);
            gradientTexture.SetPixel(0, i, color);
        }

        gradientTexture.Apply();

        for (int i = 0; i < heightTexts.Length; i++)
            heightTexts[i].color = colorGradient.Evaluate(i * 0.5f);

        Sprite gradientSprite = Sprite.Create(
            gradientTexture,
            new Rect(0, 0, gradientTexture.width, gradientTexture.height),
            new Vector2(0.5f, 0.5f)
        );

        gradientImage.sprite = gradientSprite;
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Transform hitTransform = hit.transform;
                if (selectedIsland != hitTransform.gameObject)
                {
                    Outline[] components = hitTransform.GetComponents<Outline>();
                    if (components.Length == 0)
                    {
                        Outline outline = hitTransform.AddComponent<Outline>();
                        outline.OutlineColor = outlineColor;
                        outline.OutlineWidth = 2;
                    }

                    if (selectedIsland != null)
                        selectedIsland.GetComponent<Outline>().enabled = false;

                    selectedIsland = hitTransform.gameObject;
                    confirmBtn.SetActive(true);
                    if (components.Length != 0)
                        components[0].enabled = true;
                    mousePos = hit.point;
                }
            }
            else
            {
                if (!EventSystem.current.IsPointerOverGameObject())
                    confirmBtn.SetActive(false);
                if (selectedIsland != null)
                    selectedIsland.GetComponent<Outline>().enabled = false;
                PointerEventData pointerEventData = new(eventSystem)
                {
                    position = Input.mousePosition
                };
                List<RaycastResult> results = new();
                graphicRaycaster.Raycast(pointerEventData, results);
                if (results.Where(r => r.gameObject.name == "ConfirmBtn").Count() == 0)
                    selectedIsland = null;
            }
        }
    }

    public void GenerateMap()
    {
        for (int x = 0, i = 0; x < size; x++)
            for (int y = 0; y < size; y++, i++)
                heights[x, y] = GetNoise(x, y);
    }

    float GetNoise(int x, int y)
    {
        float e = 0;
        float newX = (float)x / (scale * size) + xOffset;
        float newY = (float)y / (scale * size) + yOffset;
        float value = noise.snoise(new float2(newX, newY));
        e += Mathf.InverseLerp(0, 1, value);
        return e < 0.25f ? -1f : e;
    }

    void DetectIslands()
    {
        bool[,] visited = new bool[size, size];

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                if (!visited[x, z] && heights[x, z] > 0)
                {
                    List<Vector2Int> islandBlocks = new();
                    FloodFill(x, z, islandBlocks, visited);
                    if (!variableMode || islandBlocks.Count >= 24)
                        detectedIslands.Add(islandBlocks);
                }
            }
        }
    }

    void FloodFill(int startX, int startY, List<Vector2Int> islandBlocks, bool[,] visited)
    {
        Queue<Vector2Int> queue = new();
        queue.Enqueue(new Vector2Int(startX, startY));

        while (queue.Count > 0)
        {
            Vector2Int pos = queue.Dequeue();
            int x = pos.x, z = pos.y;

            if (x < 0 || x >= size || z < 0 || z >= size || visited[x, z] || heights[x, z] <= 0)
                continue;

            visited[x, z] = true;
            islandBlocks.Add(pos);
            queue.Enqueue(new Vector2Int(x + 1, z));
            queue.Enqueue(new Vector2Int(x - 1, z));
            queue.Enqueue(new Vector2Int(x, z + 1));
            queue.Enqueue(new Vector2Int(x, z - 1));
        }
    }

    void MapHeights()
    {
        noiseTexture = new Texture2D(size, size)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        averageIslandHeights.Clear();
        foreach (var island in detectedIslands)
        {
            float islandHeightFactor = UnityEngine.Random.Range(0.5f, 1.0f);
            int blockCount = 0;
            float heightSum = 0;
            foreach (var pos in island)
            {
                int x = pos.x;
                int z = pos.y;
                if (variableMode)
                    heights[x, z] = Mathf.Max(heights[x, z] * islandHeightFactor, 0.25f);
                Color color = colorGradient.Evaluate(heights[x, z]);
                noiseTexture.SetPixel(x, z, color);
                blockCount++;
                heightSum += heights[x, z] * 1000;
            }
            averageIslandHeights.Add(heightSum / blockCount);
        }
        noiseTexture.Apply();
        material.mainTexture = noiseTexture;
    }

    public void BuildMesh()
    {
        foreach (GameObject island in islands)
            Destroy(island);
        islands.Clear();

        for (int i = 0; i < detectedIslands.Count; i++)
        {
            Mesh mesh = new()
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            verts = new List<Vector3>();
            List<int> tris = new();
            uvs = new List<Vector2>();

            foreach (Vector2Int block in detectedIslands.ElementAt(i))
            {
                int x = block.x;
                int z = block.y;
                blockPos = new Vector3(x - 1, Mathf.Round(heights[x, z] * multiplier), z);
                int numFaces = 0;
                if (blockPos.y == 0)
                    gridZ = 1.5f;
                //build top face
                BuildFace(ref numFaces, x, z, new Vector3[]
                    { new(0, 1, 0), new (0, 1, 1), new (1, 1, 1), new (1, 1, 0) });

                //front
                if (z == 0 || heights[x, z - 1] != 0)
                    BuildFace(ref numFaces, x, z, new Vector3[]
                        { new(0, 0, 0), new (0, 1, 0), new (1, 1, 0), new (1, 0, 0) });

                //right
                if (x == size - 1 || heights[x + 1, z] != 0)
                    BuildFace(ref numFaces, x, z, new Vector3[]
                        { new(1, 0, 0), new (1, 1, 0), new (1, 1, 1), new (1, 0, 1) });

                //back
                if (z == size - 1 || heights[x, z + 1] != 0)
                    BuildFace(ref numFaces, x, z, new Vector3[]
                        { new(1, 0, 1), new (1, 1, 1), new (0, 1, 1), new (0, 0, 1) });

                //left
                if (x == 0 || heights[x - 1, z] != 0)
                    BuildFace(ref numFaces, x, z, new Vector3[]
                        { new(0, 0, 1), new (0, 1, 1), new (0, 1, 0), new (0, 0, 0) });


                int tl = verts.Count - 4 * numFaces;
                for (int j = 0; j < numFaces; j++)
                    tris.AddRange(new int[] { tl + j * 4, tl + j * 4 + 1, tl + j * 4 + 2, tl + j * 4, tl + j * 4 + 2, tl + j * 4 + 3 });
            }


            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.uv = uvs.ToArray();

            mesh.RecalculateNormals();

            GameObject islandObject = new(averageIslandHeights.ElementAt(i).ToString());
            islandObject.transform.parent = transform;

            MeshFilter meshFilter = islandObject.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            MeshRenderer meshRenderer = islandObject.AddComponent<MeshRenderer>();
            meshRenderer.material = material;

            MeshCollider meshCollider = islandObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;


            islandObject.transform.Rotate(new Vector3(-90, 0, 0));
            islandObject.transform.localScale = new Vector3(1, 1, 1);
            islandObject.transform.localPosition = new Vector3(0, 0, 0);
            islands.Add(islandObject);
        }
    }

    void BuildFace(ref int numFaces, int x, int y, Vector3[] vector3s)
    {
        for (int i = 0; i < 4; i++)
            verts.Add(blockPos + vector3s[i]);

        numFaces++;

        uvs.Add(new Vector2(x / (float)size, y / (float)size));
        uvs.Add(new Vector2(x / (float)size, (y + 1) / (float)size));
        uvs.Add(new Vector2((x + 1) / (float)size, (y + 1) / (float)size));
        uvs.Add(new Vector2((x + 1) / (float)size, y / (float)size));
    }

    public void OnSliderValueChanged(float value)
    {
        size = (int)value;
        heights = new float[size, size];
        detectedIslands.Clear();

        if (variableMode)
        {
            scale = 0.3f - ((size - 65) / 425f);
            GenerateMap();
        }
        else
        {
            ParseHeightsText(webMatrixString);
            gridZ = 5f;
        }
        DetectIslands();
        MapHeights();
        BuildMesh();

        float mapScale = 100f / size;
        transform.localScale = new Vector3(mapScale, mapScale, mapScale);
        if (variableMode)
        {
            transform.position = new Vector3(AdjustCoord(size, -48.45f, -49.05f, -49.335f), -50, AdjustCoord(size, 1.75f, 0.75f, 0.3f));
            handleText.text = size.ToString();
            confirmBtn.SetActive(false);
        }
        else
            transform.position = new Vector3(-46.85f, -50, gridZ);
    }

    float AdjustCoord(float size, float start, float middle, float end)
    {
        if (size <= 107)
            return Mathf.Lerp(start, middle, (size - 65) / (107 - 65));
        else
            return Mathf.Lerp(middle, end, (size - 107) / (150 - 107));
    }

    public void RandomizeOffset()
    {
        if (!variableMode)
            StartCoroutine(GetRequest("https://jobfair.nordeus.com/jf24-fullstack-challenge/test"));
        else
        {
            xOffset = UnityEngine.Random.Range(-size, size);
            yOffset = UnityEngine.Random.Range(-size, size);
            OnSliderValueChanged(size);
        }
    }

    public void SinkIsland()
    {
        confirmBtn.SetActive(false);
        randomizeBtn.SetActive(false);
        slider.SetActive(false);
        float height = float.Parse(selectedIsland.name);
        StartCoroutine(SinkSelectedIsland(selectedIsland));
        if (tries > 0)
        {
            PlayerPrefs.SetInt("guesses", PlayerPrefs.GetInt("guesses") + 1);
            tries--;
            float maxHeight = averageIslandHeights.Max();
            if (maxHeight > height)
            {
                if (tries != 0)
                {
                    if (tries == 1)
                        triesLeftText.text = tries + " try left!";
                    else
                        triesLeftText.text = tries + " tries left!";
                    triesLeftText.GetComponent<Animator>().Play("triesLeft");
                }
                else
                {
                    endText.text = "You lost!";
                    endTriesLeftText.text = "Max avg height of island: " + Mathf.RoundToInt(maxHeight);
                    endPanel.SetActive(true);
                }
            }
            else
            {
                endText.text = "You won!";
                endTriesLeftText.text = "Tries left: " + tries;
                endPanel.SetActive(true);
                PlayerPrefs.SetInt("correct", PlayerPrefs.GetInt("correct") + 1);
                foreach (GameObject island in islands)
                    StartCoroutine(SinkSelectedIsland(island, false));
            }

        }
    }

    public void ResetGame()
    {
        SceneManager.LoadScene("Scene");
    }

    void ParseHeightsText(string text)
    {
        string[] rows = text.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        int numRows = rows.Length;
        int numCols = rows[0].Trim().Split(' ').Length;

        for (int x = 0; x < numRows; x++)
        {
            string[] cols = rows[x].Trim().Split(' ');
            for (int z = 0; z < numCols; z++)
            {
                float height = float.Parse(cols[z]);
                heights[x, z] = height > 0 ? height / 1000 : -1;
            }
        }
    }

    private IEnumerator SinkSelectedIsland(GameObject island, bool instantiate = true)
    {
        Vector3 islandPos = mousePos;
        while (island.transform.localPosition.y > -4.5f)
        {
            Vector3 position = island.transform.localPosition;
            position.y -= 0.1f;
            island.transform.localPosition = position;

            yield return new WaitForSeconds(0.05f);
        }
        if (instantiate)
        {
            TextMeshProUGUI text = Instantiate(avgHeightText, new Vector3(islandPos.x, islandPos.y, -5f), Quaternion.identity, graphicRaycaster.transform);
            text.text = Mathf.RoundToInt(float.Parse(island.name)).ToString();
        }
    }

    IEnumerator GetRequest(string url)
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                string newMatrixString = webRequest.downloadHandler.text;
                if (webRequest.downloadHandler.text.Equals(webMatrixString))
                    StartCoroutine(GetRequest("https://jobfair.nordeus.com/jf24-fullstack-challenge/test"));
                webMatrixString = webRequest.downloadHandler.text;
                OnSliderValueChanged(size);
            }
            else
                Debug.LogError($"Error: {webRequest.error}");
        }
    }
}
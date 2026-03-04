using UnityEngine;

public class MazeCell : MonoBehaviour
{
    #region VARIABLES

    public bool isVisited = false;
    public GameObject cellObj;

    // Estado de los items en esta celda (los rellena Perlin Noise)
    public bool hasCoin = false;
    public bool hasPowerUp = false;

    // Constructor
    public MazeCell(int x, int y, GameObject obj)
    {
        cellObj = obj;
    }

    #endregion

    /// <summary>
    /// Desactiva la pared indicada por nombre: Wall_N, Wall_S, Wall_E, Wall_W
    /// Igual que en el ejercicio del laberinto.
    /// </summary>
    public void RemoveWall(string wallName)
    {
        Transform wall = cellObj.transform.Find(wallName);
        if (wall != null)
        {
            wall.gameObject.SetActive(false);
        }
    }
}


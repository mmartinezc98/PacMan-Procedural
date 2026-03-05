using UnityEngine;

public class MazeCell
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
        if (cellObj == null)
        {
            Debug.LogError("MazeCell: cellObj es null!");
            return;
        }

        Transform wall = cellObj.transform.Find(wallName);
        if (wall != null)
        {
            wall.gameObject.SetActive(false);
        }
        else
        {
            // Si no encuentra la pared, listamos los hijos para ver sus nombres reales
            Debug.LogWarning("MazeCell: no se encontro '" + wallName + "' en " + cellObj.name
                + ". Hijos encontrados:");
            foreach (Transform child in cellObj.transform)
                Debug.LogWarning("  - '" + child.name + "'");
        }
    }
}

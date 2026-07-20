using UnityEngine;
using TMPro;

/*
挂到所有实验仪器上。
功能：
✅ 鼠标进入显示
✅ 自动检测模型高度
✅ 显示在物体顶部
✅ 不跟随鼠标
✅ 自动适配文字大小
✅ 描边支持
 * */

public class HoverInfoDisplay : MonoBehaviour
{


    [Header("仪器信息")]

    public string objectName;


    [TextArea(3, 6)]
    public string description;



    [Header("UI")]

    public GameObject hoverContainer;

    public TextMeshProUGUI hoverText;


    private RectTransform hoverRect;



    [Header("显示参数")]

    public float maxWidth = 350f;

    public float padding = 40f;

    public float heightOffset = 50f;



    private Camera cam;


    private Outline outline;



    void Start()
    {

        cam = Camera.main;


        if (hoverContainer)
        {
            hoverRect =
            hoverContainer.GetComponent<RectTransform>();


            hoverContainer.SetActive(false);
        }



        outline =
        GetComponent<Outline>();

    }



    void OnMouseEnter()
    {

        //Show();


        if (outline)
            outline.enabled = true;

    }



    void OnMouseExit()
    {

        //if (hoverContainer)
           // hoverContainer.SetActive(false);



        if (outline)
            outline.enabled = false;

    }

    void OnMouseDown()
    {

        //先显示仪器信息（如果需要）
        //Show();


        //检查实验管理器
        if (ExperimentManager.Instance == null)
        {
            Debug.Log("实验尚未开始");
            return;
        }



        //获取当前步骤

        ExperimentStep step =
        ExperimentManager.Instance.CurrentStep();



        //没有开始实验

        if (step == null)
        {

            Debug.Log("请先点击开始实验按钮");
            return;

        }



        //交给当前步骤处理

        step.HandleObjectClick(this);

    }

    void Show()
    {

        hoverText.text =objectName+"\n\n"+description;



        hoverText.ForceMeshUpdate();


        Vector2 size =
        hoverText.GetPreferredValues(
            hoverText.text,
            maxWidth,
            Mathf.Infinity);



        hoverRect.sizeDelta =
        new Vector2(
            size.x + padding,
            size.y + padding);



        hoverContainer.SetActive(true);



        PlaceAboveObject();

    }




    void PlaceAboveObject()
    {


        Renderer r =
        GetComponentInChildren<Renderer>();


        Vector3 top;



        if (r)
        {
            top =
            new Vector3(
                r.bounds.center.x,
                r.bounds.max.y,
                r.bounds.center.z
            );
        }
        else
        {
            top =
            transform.position;
        }



        Vector3 screen =
        cam.WorldToScreenPoint(top);



        hoverRect.position =
        new Vector3(

            screen.x,

            screen.y
            +
            hoverRect.sizeDelta.y / 2
            +
            heightOffset,

            0
        );

    }

}
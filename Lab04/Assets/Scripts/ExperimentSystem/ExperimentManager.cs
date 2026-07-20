using UnityEngine;
using TMPro;


public class ExperimentManager : MonoBehaviour
{

    public static ExperimentManager Instance;


    [Header("实验步骤")]
    public ExperimentStep[] steps;



    [Header("当前步骤显示")]
    public TMP_Text stepText;



    private int currentIndex = 0;



    private bool experimentStarted = false;



    void Awake()
    {
        Instance = this;
    }



    void Start()
    {

        // 不自动开始

        if (stepText != null)
        {
            stepText.text =
            "请点击开始实验";
        }

    }



    //开始按钮调用这个
    public void StartExperiment()
    {

        if (experimentStarted)
            return;


        experimentStarted = true;


        currentIndex = 0;


        StartCurrentStep();

    }

    public void FinishExperiment()
    {

        if (!experimentStarted)
        {
            stepText.text = "实验还未开始";
            return;
        }



        //检查所有步骤

        for (int i = 0; i < steps.Length; i++)
        {

            if (!steps[i].IsCompleted())
            {

                stepText.text =
                "还有步骤未完成：\n"
                +
                steps[i].stepName;


                Debug.Log(
                "未完成步骤:"
                +
                steps[i].stepName
                );


                return;

            }

        }



        //全部完成

        stepText.text =
        "实验结束";


        Debug.Log(
        "实验全部完成"
        );


    }

    void StartCurrentStep()
    {


        if (currentIndex >= steps.Length)
        {

            stepText.text =
            "实验完成";


            return;

        }



        steps[currentIndex]
        .StartStep();



        UpdateStepUI();

    }





    public void NextStep()
    {


        currentIndex++;



        StartCurrentStep();


    }




    public ExperimentStep CurrentStep()
    {

        if (!experimentStarted)
            return null;


        return steps[currentIndex];

    }



    void UpdateStepUI()
    {


        if (stepText == null)
            return;



        stepText.text =
        "当前步骤：\n"
        +
        steps[currentIndex].stepName;


    }




    public void NotifyObserve(
        HoverInfoDisplay obj)
    {


        if (!experimentStarted)
            return;



        Step1_Observe step =
        CurrentStep() as Step1_Observe;



        if (step != null)
        {

            step.ObserveObject(obj);

        }


    }


}
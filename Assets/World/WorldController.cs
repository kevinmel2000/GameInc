﻿using System;
using Database;
using UnityEngine;

public class WorldController : MonoBehaviour {
    [SerializeField] private World world;
    [SerializeField] private GameDevCompany playerCompany;
    [SerializeField] private EngineFeaturesController engineFeaturesController;
    [SerializeField] private EventsController eventsController;
    [SerializeField] private NewsController newsController;
    [SerializeField] private GameHudController hudController;

    public void ToggleSimulationPause() {
        world.ToggleSimulation();
    }

    public void ToggleSimulationSpeed() {
        world.ToggleSimulationSpeed();
    }

    public void BuildRoom(string roomId) {
        world.BuildNewRoom(roomId);
    }

    public void OnGameStarted(Database.Database database, DateTime gameDateTime, GameDevCompany playerCompany) {
        this.playerCompany = playerCompany;
        engineFeaturesController.InitFeatures(database.EngineFeatures.Collection);
        engineFeaturesController.CheckFeatures(eventsController, gameDateTime, playerCompany);
        eventsController.InitEvents(database.Events.Collection);
        eventsController.InitVariables(gameDateTime, playerCompany);
        newsController.InitNews(database.News.Collection, gameDateTime);
    }

    public void OnDateModified(DateTime gameDateTime) {
        eventsController.OnGameDateChanged(gameDateTime, playerCompany);
        News latestNews = newsController.OnGameDateChanged(gameDateTime);
        if (latestNews != null)
            hudController.UpdateNewsBar(latestNews);
        hudController.UpdateDateDisplay(gameDateTime);
    }

    public void OnPlayerCompanyModified(DateTime gameDateTime) {
        eventsController.OnPlayerCompanyChanged(gameDateTime, playerCompany);
        hudController.UpdateCompanyHud(playerCompany);
    }

    public void OnProjectCompleted(DateTime gameDateTime,
        GameDevCompany company, Project project) {
        if (project.Type() == Project.ProjectType.GameProject)
            engineFeaturesController.CheckFeatures(eventsController,
                gameDateTime, company);
    }

    public void OnConstructionStarted(float constructionCost) {
        world.OnConstructionStarted(constructionCost);
    }
}
// Groovy Script: http://www.groovy-lang.org/syntax.html
// Jenkins DSL: https://github.com/jenkinsci/job-dsl-plugin/wiki

// Import the pipeline declaration classes.
import org.dotnet.ci.pipelines.Pipeline

// The input project name (e.g. dotnet/diagnostics)
def project = GithubProject

// The input branch name (e.g. master)
def branch = GithubBranchName

class Constants {

    def static osList = [
        'Windows_NT',
//        'Ubuntu',
        'Ubuntu16.04',
//        'Ubuntu16.10',
//        'Debian8.4',
        'CentOS7.1',
//        'RHEL7.2',
//        'Fedora24'
    ]

    def static configurationList = [
//        'Debug', 
        'Release'
    ]

    // This is the set of architectures
    def static architectureList = [
//        'arm', 
//        'arm64', 
        'x64', 
//        'x86'
    ]

}

// Create build and test pipeline job
def pipeline = Pipeline.createPipeline(this, project, branch, 'pipeline.groovy')

Constants.osList.each { os ->
    Constants.architectureList.each { architechure ->
        Constants.configurationList.each { configuration ->
            def triggerName = "${os} ${architechure} ${configuration} Build and Test"
            def params = ['OS':os, 'Architechure':architechure, 'Configuration':configuration]

            pipeline.triggerPipelineOnEveryPR(triggerName, params)

            // Add trigger to run on merge
            pipeline.triggerPipelineOnPush(params)
        }
    }
}
